namespace StarBlog.Web.ViewModels.Auth;

public class LoginToken {
    public string Token { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
    public bool MustChangePassword { get; set; }
}
