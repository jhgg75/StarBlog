namespace StarBlog.Testing;

public sealed class TempWorkspace : IDisposable {
    public string RootPath { get; }

    public TempWorkspace(string  prefix = null) {
        prefix   = "starblog-tests";
        RootPath = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string GetPath(params string[] parts) {
        if (parts.Length == 0) return RootPath;
        return Path.Combine(new[] { RootPath }.Concat(parts).ToArray());
    }

    public void Dispose() {
        try {
            if (Directory.Exists(RootPath)) {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch {
        }
    }
}
