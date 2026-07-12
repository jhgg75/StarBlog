namespace StarBlog.Application.Models.Config;

public class BootstrapOptions {
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; }

    public bool RunLogDatabaseMigrations { get; set; } = true;

    public bool SeedAdmin { get; set; }

    public string AdminUsername { get; set; } = "admin";

    public string? AdminPassword { get; set; }
}
