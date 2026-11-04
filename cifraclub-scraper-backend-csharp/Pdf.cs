using Microsoft.Playwright;

namespace cifraclub_scraper_backend_csharp;

public static class Pdf
{
    public static async Task HtmlToPdf(string html, string path)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu"
            }
        });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new PageSetContentOptions
        {
            WaitUntil = WaitUntilState.Load
        });
        await page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            Media = Media.Print
        });
        await page.PdfAsync(new PagePdfOptions
        {
            Path = path,
            Format = "A4",
            PrintBackground = true,
            PreferCSSPageSize = true,
            Margin = new Margin
            {
                Top = "15mm",
                Bottom = "15mm",
                Left = "15mm",
                Right = "12mm"
            }
        });
    }
}
