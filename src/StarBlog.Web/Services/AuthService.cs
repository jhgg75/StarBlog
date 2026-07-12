using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FreeSql;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StarBlog.Content.Extensions;
using StarBlog.Data.Models;
using StarBlog.Web.Models.Config;
using StarBlog.Web.ViewModels.Auth;

namespace StarBlog.Web.Services;

[ScopedDependency]
public class AuthService {
    private readonly Auth _auth;
    private readonly IBaseRepository<User> _userRepo;

    private const string ClaimUserId = "user_id";
    private const string ClaimUserName = "user_name";
    private const string ClaimMustChangePassword = "must_change_password";

    public AuthService(IOptions<Auth> options, IBaseRepository<User> userRepo) {
        _auth = options.Value;
        _userRepo = userRepo;
    }

    public LoginToken GenerateLoginToken(User user) {
        var claims = new List<Claim> {
            new(ClaimUserId, user.Id), // User.Identity.Name
            new(ClaimUserName, user.Name),
            new(ClaimMustChangePassword, user.MustChangePassword.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_auth.Jwt.Key));
        // todo 使用非对称加密 jwt (RSA)
        var signCredential = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwtToken = new JwtSecurityToken(
            issuer: _auth.Jwt.Issuer,
            audience: _auth.Jwt.Audience,
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: signCredential
        );

        // todo 尝试使用 jose-jwt 生成 jwt
        return new LoginToken {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
            Expiration = TimeZoneInfo.ConvertTimeFromUtc(jwtToken.ValidTo, TimeZoneInfo.Local),
            MustChangePassword = user.MustChangePassword
        };
    }

    public async Task<LoginToken> ChangePasswordAsync(string userId, string currentPassword, string newPassword) {
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword)) {
            throw new InvalidOperationException("当前密码和新密码不能为空");
        }

        var user = await GetUserById(userId);
        if (user == null) {
            throw new InvalidOperationException("找不到当前用户");
        }

        if (currentPassword.ToSHA256() != user.Password) {
            throw new UnauthorizedAccessException("当前密码不正确");
        }

        user.Password = newPassword.ToSHA256();
        user.MustChangePassword = false;
        user.LastPasswordChangeTime = DateTime.UtcNow;
        await _userRepo.UpdateAsync(user);
        return GenerateLoginToken(user);
    }

    public async Task<User > GetUserById(string userId) {
        return await _userRepo.Where(a => a.Id == userId).FirstAsync();
    }

    public async Task<User > GetUserByName(string name) {
        return await _userRepo.Where(a => a.Name == name).FirstAsync();
    }

    public User  GetUser(ClaimsPrincipal userClaim) {
        var userId = userClaim.FindFirstValue(ClaimUserId);
        var userName = userClaim.FindFirstValue(ClaimUserName);
        var mustChangePassword = bool.TryParse(userClaim.FindFirst(ClaimMustChangePassword)?.Value, out var parsed)
            && parsed;
        if (userId == null || userName == null) return null;
        return new User { Id = userId, Name = userName, MustChangePassword = mustChangePassword };
    }
}
