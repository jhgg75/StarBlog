using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using RobotsTxt;
using Serilog;
using Serilog.Events;
using SixLabors.ImageSharp.Web.DependencyInjection;
using StarBlog.Application.Abstractions;
using StarBlog.Application.Models.Config;
using StarBlog.Data;
using StarBlog.Data.Extensions;
using StarBlog.Application.Services;
using StarBlog.Web.Contrib.SiteMessage;
using StarBlog.Web.Extensions;
using StarBlog.Web.Filters;
using StarBlog.Web.Middlewares;
using StarBlog.Web.Services;
using StarBlog.Web.Services.BackgroundTasks;
using StarBlog.Web.Services.OutboxServices;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
    builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder => {
        containerBuilder.RegisterModule(new AutofacDependencyModule(
            typeof(Program).Assembly,
            typeof(StarBlog.Application.Services.BlogService).Assembly
        ));
    });

    builder.Host.UseSerilog((context, services, loggerConfiguration) => {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);
    });

    var mvcBuilder = builder.Services.AddControllersWithViews(options => { options.Filters.Add<ResponseWrapperFilter>(); });

    if (builder.Environment.IsDevelopment()) {
        mvcBuilder.AddRazorRuntimeCompilation();
    }

    builder.Services.AddMemoryCache();
    builder.Services.AddHttpContextAccessor();

    builder.Services.AddResponseCompression(options => {
        options.EnableForHttps = true;
        options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
        options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
        options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(new[] {
            "application/javascript",
            "application/json",
            "application/xml",
            "text/css",
            "text/html",
            "text/json",
            "text/plain",
            "text/xml"
        });
    });

    builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options => {
        options.Level = System.IO.Compression.CompressionLevel.Optimal;
    });

    builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options => {
        options.Level = System.IO.Compression.CompressionLevel.Optimal;
    });

    builder.Services.AddSession(options => {
        options.IdleTimeout = TimeSpan.FromHours(1);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });
    builder.Services.AddAutoMapper(typeof(Program));
    builder.Services.AddDbContext<AppDbContext>(options => {
        options.UseSqlite(builder.Configuration.GetConnectionString("SQLite-Log"));
    });
    builder.Services.AddFreeSql(builder.Configuration);
    builder.Services.AddVisitRecord();
    builder.Services.AddHttpClient();
    builder.Services.AddCors(options => {
        options.AddDefaultPolicy(policyBuilder => {
            policyBuilder.AllowCredentials();
            policyBuilder.AllowAnyHeader();
            policyBuilder.AllowAnyMethod();
            policyBuilder.WithOrigins(
                "http://localhost:3000",
                "http://localhost:8080",
                "http://localhost:8081",
                "https://deali.cn",
                "https://blog.deali.cn"
            );
        });
    });
    builder.Services.AddStaticRobotsTxt(opt => {
        var baseUrl = builder.Configuration["host"] ?? "https://blog.deali.cn";
        opt.AddSection(section => section.AddUserAgent("Googlebot").Allow("/"))
            .AddSection(section => section.AddUserAgent("bingbot").Allow("/"))
            .AddSection(section => section.AddUserAgent("Bytespider").Disallow("/"))
            .AddSection(section => section.AddUserAgent("Sogou web spider").Allow("/"))
            .AddSection(section => section.AddUserAgent("*")
                .Disallow("/Admin/")
                .Disallow("/Api/")
                .Disallow("/bin/")
                .Disallow("/obj/")
                .Disallow("/node_modules/")
                .Disallow("/seo-test")
                .Allow("/"))
            .AddSitemap($"{baseUrl}/sitemap.xml")
            .AddSitemap($"{baseUrl}/sitemap-images.xml");

        return opt;
    });
    builder.Services.AddSwagger();
    builder.Services.AddSettings(builder.Configuration);
    builder.Services.AddAuth(builder.Configuration);
    builder.Services.AddHttpClient();
    builder.Services.AddImageSharp();
    builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));

    builder.Services.Configure<TranslationConfig>(builder.Configuration.GetSection(TranslationConfig.SectionName));

    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
    builder.Services.AddHostedService<OutboxWorker>();
    builder.Services.AddHostedService<BackgroundTaskWorker>();

    builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = long.MaxValue; });

    var app = builder.Build();

    await using (var scope = app.Services.CreateAsyncScope()) {
        var startupBootstrapService = scope.ServiceProvider.GetRequiredService<StartupBootstrapService>();
        await startupBootstrapService.RunAsync();
    }

    if (app.Environment.IsDevelopment()) {
        app.UseDeveloperExceptionPage();
    }
    else {
        app.UseExceptionHandler(applicationBuilder => {
            applicationBuilder.Run(async context => {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { message = "Unexpected error!" });
            });
        });
        app.UseHsts();
    }

    app.UseForwardedHeaders(new ForwardedHeadersOptions {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

    app.UseSerilogRequestLogging(options => {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, _, exception) => {
            if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError) {
                return LogEventLevel.Error;
            }

            if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest) {
                return LogEventLevel.Warning;
            }

            return LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) => {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        };
    });

    app.UseImageSharp();
    // app.UseHttpsRedirection();
    app.UseResponseCompression();

    if (!app.Environment.IsProduction()) {
        app.UseMiddleware<StaticFileDebugMiddleware>();
    }

    app.UseStaticFiles(new StaticFileOptions {
        ServeUnknownFileTypes = true,
        OnPrepareResponse = ctx => {
            var path = ctx.Context.Request.Path.Value;

            if (path != null && (path.EndsWith(".js") || path.EndsWith(".css"))) {
                if (app.Environment.IsDevelopment()) {
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers.Pragma = "no-cache";
                    ctx.Context.Response.Headers.Expires = "0";
                }
                else {
                    const int shortDuration = 60 * 60 * 24;
                    ctx.Context.Response.Headers.CacheControl = $"public,max-age={shortDuration}";
                    ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddDays(1).ToString("R");
                }
            }
            else {
                const int durationInSeconds = 60 * 60 * 24 * 30;
                ctx.Context.Response.Headers.CacheControl = $"public,max-age={durationInSeconds}";
                ctx.Context.Response.Headers.Expires = DateTime.UtcNow.AddDays(30).ToString("R");
            }
        }
    });

    app.UseMiddleware<VisitRecordMiddleware>();
    app.UseRobotsTxt();
    app.UseRouting();
    app.UseCors();
    app.UseAuthentication();
    app.UseMiddleware<RequirePasswordChangeMiddleware>();
    app.UseAuthorization();
    app.UseSession();
    app.UseSwaggerPkg();

    app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex) {
    Log.Fatal(ex, "StarBlog.Web terminated unexpectedly");
}
finally {
    Log.CloseAndFlush();
}

public partial class Program { }
