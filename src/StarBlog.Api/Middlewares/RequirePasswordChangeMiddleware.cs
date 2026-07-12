using System.Security.Claims;

namespace StarBlog.Api.Middlewares;

public class RequirePasswordChangeMiddleware {
    private readonly RequestDelegate _next;

    public RequirePasswordChangeMiddleware(RequestDelegate next) {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context) {
        if (context.User.Identity?.IsAuthenticated == true &&
            bool.TryParse(context.User.FindFirstValue("must_change_password"), out var mustChangePassword) &&
            mustChangePassword &&
            !context.Request.Path.StartsWithSegments("/Api/Auth")) {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new {
                message = "当前账号使用的是临时密码，请先修改密码后再继续操作。"
            });
            return;
        }

        await _next(context);
    }
}
