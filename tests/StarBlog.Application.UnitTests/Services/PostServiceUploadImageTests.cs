using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StarBlog.Application.Abstractions;
using StarBlog.Application.Services;
using StarBlog.Data.Extensions;
using StarBlog.Data.Models;
using StarBlog.Testing;

namespace StarBlog.Application.UnitTests.Services;

public sealed class PostServiceUploadImageTests {
    [Fact]
    public async Task UploadImage_SavesFile_UnderWebRoot_AndReturnsAbsoluteUrl() {
        using var workspace = new TempWorkspace("starblog-application-tests");
        var webRoot = workspace.GetPath("wwwroot");
        Directory.CreateDirectory(webRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string > {
                ["ConnectionStrings:SQLite"] = $"Data Source={workspace.GetPath("app.data.db")}",
                ["StarBlog:Initial:host"] = "http://localhost"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpClient();
        services.AddFreeSql(configuration);

        services.AddSingleton<IAppPathProvider>(new TestAppPathProvider(webRoot));
        services.AddSingleton<IFileStorage>(sp => new TestFileStorage(sp.GetRequiredService<IAppPathProvider>()));
        services.AddSingleton<IClock, TestClock>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

        services.AddSingleton<CommonService>();
        services.AddScoped<ConfigService>();
        services.AddScoped<PostService>();

        await using var serviceProvider = services.BuildServiceProvider();

        var postService = serviceProvider.GetRequiredService<PostService>();
        var post = new Post { Id = "p1", Title = "t1" };

        await using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        var url = await postService.UploadImage(post, input, "a.png");

        Assert.StartsWith("http://localhost/media/blog/p1/", url.Replace('\\', '/'));

        var dir = Path.Combine(webRoot, "media", "blog", "p1");
        var files = Directory.GetFiles(dir);
        Assert.Single(files);
        Assert.EndsWith(".png", files[0], StringComparison.OrdinalIgnoreCase);

        var bytes = await File.ReadAllBytesAsync(files[0]);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    private sealed class TestAppPathProvider : IAppPathProvider {
        public TestAppPathProvider(string webRootPath) {
            WebRootPath = webRootPath;
        }

        public string WebRootPath { get; }
    }

    private sealed class TestClock : IClock {
        public DateTime UtcNow => new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        public DateTime Now => new(2020, 1, 2, 3, 4, 5, DateTimeKind.Local);
    }

    private sealed class TestFileStorage : IFileStorage {
        private readonly IAppPathProvider _paths;

        public TestFileStorage(IAppPathProvider paths) {
            _paths = paths;
        }

        public Task EnsureDirectoryAsync(string relativeDirectory, CancellationToken cancellationToken = default) {
            Directory.CreateDirectory(MapPath(relativeDirectory));
            return Task.CompletedTask;
        }

        public async Task SaveAsync(string relativePath, Stream content, bool overwrite = true, CancellationToken cancellationToken = default) {
            var fullPath = MapPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }

            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            await using var fs = new FileStream(fullPath, mode, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(fs, cancellationToken);
        }

        public Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory, CancellationToken cancellationToken = default) {
            var dir = MapPath(relativeDirectory);
            if (!Directory.Exists(dir)) {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            var files = Directory.GetFiles(dir)
                .Select(f => Path.GetFileName(f)!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            return Task.FromResult<IReadOnlyList<string>>(files);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default) {
            return Task.FromResult(File.Exists(MapPath(relativePath)));
        }

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) {
            var fullPath = MapPath(relativePath);
            if (File.Exists(fullPath)) {
                File.Delete(fullPath);
            }
            return Task.CompletedTask;
        }

        private string MapPath(string relativePath) {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized)) {
                return normalized;
            }
            return Path.Combine(_paths.WebRootPath, normalized);
        }
    }
}
