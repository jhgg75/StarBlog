using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarBlog.Application.Models.Config;
using StarBlog.Data;

namespace StarBlog.Application.Services;

[ScopedDependency]
public class StartupBootstrapService {
    private readonly AppDbContext _appDbContext;
    private readonly AdminAccountInitializer _adminAccountInitializer;
    private readonly BootstrapOptions _options;
    private readonly ILogger<StartupBootstrapService> _logger;

    public StartupBootstrapService(
        AppDbContext appDbContext,
        AdminAccountInitializer adminAccountInitializer,
        IOptions<BootstrapOptions> options,
        ILogger<StartupBootstrapService> logger) {
        _appDbContext = appDbContext;
        _adminAccountInitializer = adminAccountInitializer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync() {
        if (!_options.Enabled) {
            _logger.LogInformation("已跳过启动初始化作业，当前实例未启用 Bootstrap。");
            return;
        }

        if (_options.RunLogDatabaseMigrations) {
            await _appDbContext.Database.MigrateAsync();
        }

        if (_options.SeedAdmin) {
            await _adminAccountInitializer.EnsureDefaultAdminAsync();
        }
    }
}
