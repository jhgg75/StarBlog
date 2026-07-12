using FreeSql;
using StarBlog.Application.Models.Config;
using StarBlog.Content.Extensions;
using StarBlog.Data.Models;

namespace StarBlog.Application.Services;

[ScopedDependency]
public class AdminAccountInitializer {
    private readonly IBaseRepository<User> _userRepo;
    private readonly BootstrapOptions _options;
    private readonly ILogger<AdminAccountInitializer> _logger;

    public AdminAccountInitializer(
        IBaseRepository<User> userRepo,
        IOptions<BootstrapOptions> options,
        ILogger<AdminAccountInitializer> logger) {
        _userRepo = userRepo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureDefaultAdminAsync() {
        var userCount = await _userRepo.Select.CountAsync();
        if (userCount > 0) {
            return;
        }

        var adminUsername = Environment.GetEnvironmentVariable("STARBLOG_BOOTSTRAP_ADMIN_USERNAME")
                            ?? _options.AdminUsername;
        var adminPassword = Environment.GetEnvironmentVariable("STARBLOG_BOOTSTRAP_ADMIN_PASSWORD")
                            ?? _options.AdminPassword;

        if (string.IsNullOrWhiteSpace(adminPassword)) {
            throw new InvalidOperationException(
                "已启用管理员初始化，但未配置初始管理员密码。请通过配置项 Bootstrap:AdminPassword 或环境变量 STARBLOG_BOOTSTRAP_ADMIN_PASSWORD 提供密码。");
        }

        var admin = new User {
            Id = Guid.NewGuid().ToString(),
            Name = adminUsername,
            Password = adminPassword.ToSHA256(),
            MustChangePassword = true
        };

        await _userRepo.InsertAsync(admin);
        _logger.LogWarning("未检测到任何后台用户，已自动创建初始管理员账号 {AdminUser}。该账号使用的是临时密码，首次登录后必须立即修改密码。", adminUsername);
    }
}
