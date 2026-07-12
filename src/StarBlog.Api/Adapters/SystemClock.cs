using StarBlog.Application.Abstractions;

namespace StarBlog.Api.Adapters;

[SingletonDependency]
public sealed class SystemClock : IClock {
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
}
