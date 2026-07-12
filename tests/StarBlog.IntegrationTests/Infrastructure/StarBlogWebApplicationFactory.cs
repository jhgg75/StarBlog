using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StarBlog.Testing;
using StarBlog.Web.Services.OutboxServices;

namespace StarBlog.IntegrationTests.Infrastructure;

public sealed class StarBlogWebApplicationFactory : WebApplicationFactory<Program> {
    private readonly TempWorkspace _workspace = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        var dataDbPath = _workspace.GetPath("app.data.db");
        var logDbPath = _workspace.GetPath("app.log.db");

        var config = new Dictionary<string, string> {
            ["host"] = "http://localhost",
            ["ConnectionStrings:SQLite"] = $"Data Source={dataDbPath}",
            ["ConnectionStrings:SQLite-Log"] = $"Data Source={logDbPath}"
        };

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) => {
            configurationBuilder.AddInMemoryCollection(config);
        });

        builder.ConfigureServices(services => {
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService)).ToList()) {
                if (descriptor.ImplementationType == typeof(OutboxWorker)) {
                    services.Remove(descriptor);
                }
            }

            services.AddAuthentication(options => {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);

        if (!disposing) return;
        _workspace.Dispose();
    }

    
}
