using Microsoft.Playwright;

namespace StarBlog.E2ETests;

public sealed class BasicNavigationTests {
    [Fact]
    public async Task HomePage_HasStarBlogTitle() {
        if (!E2EBaseUrl.TryGet(out var baseUrl)) return;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
            Headless = true
        });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{baseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var title = await page.TitleAsync();

        Assert.Contains("StarBlog", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RobotsTxt_ContainsSitemap() {
        if (!E2EBaseUrl.TryGet(out var baseUrl)) return;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
            Headless = true
        });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{baseUrl}/robots.txt", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var text = await page.EvaluateAsync<string>("() => document.body   document.body.innerText : ''");

        Assert.Contains("Sitemap:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/sitemap.xml", text, StringComparison.OrdinalIgnoreCase);
    }
}
