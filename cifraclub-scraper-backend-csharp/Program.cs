using System.Net;
using System.Net.Security;
using cifraclub_scraper_backend_csharp;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "CifraClub Scraper API",
        Version = "v1",
        Description = "API to scrape songs and generate print-ready chord sheets / PDFs from CifraClub."
    });
});

var cookieContainer = new CookieContainer();

builder.Services.AddHttpClient<Scraper>(client =>
{
    client.DefaultRequestVersion = HttpVersion.Version20;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
    client.DefaultRequestHeaders.Clear();
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,es;q=0.8,pt;q=0.7");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
    client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
    client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
    client.Timeout = TimeSpan.FromSeconds(60);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    CookieContainer = cookieContainer,
    UseCookies = true
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CifraClub Scraper API v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Ok(new
{
    status = "healthy",
    service = "cifraclub-scraper-backend-csharp",
    endpoints = new
    {
        scrape_post = "POST /scrape",
        scrape_get = "GET /scrape?url={url}&limit={limit}",
        pdf_post = "POST /pdf (body: { url, limit, html, outputPath })",
        pdf_get = "GET /pdf?url={url}&limit={limit}",
        swagger = "GET /swagger"
    }
}))
.WithName("HealthCheck")
.WithTags("System");

app.MapPost("/scrape", async ([FromBody] ScrapeRequest request, [FromServices] Scraper scraper, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "The 'url' parameter is required." });
    }

    try
    {
        var response = await scraper.ScrapeAsync(request.Url, request.Limit, ct);
        if (response.Total == 0)
        {
            return Results.NotFound(new { error = "No songs found for the provided URL.", url = request.Url });
        }

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Scraping failed", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("ScrapeSongsPost")
.WithTags("Scraper");

app.MapGet("/scrape", async ([FromQuery] string? url, [FromQuery] int? limit, [FromServices] Scraper scraper, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(url))
    {
        return Results.BadRequest(new { error = "The 'url' query parameter is required." });
    }

    try
    {
        var response = await scraper.ScrapeAsync(url, limit, ct);
        if (response.Total == 0)
        {
            return Results.NotFound(new { error = "No songs found for the provided URL.", url });
        }

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Scraping failed", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("ScrapeSongsGet")
.WithTags("Scraper");

app.MapGet("/pdf", async (HttpContext httpContext, [FromQuery] string? url, [FromQuery] int? limit, [FromServices] Scraper scraper, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(url))
    {
        return Results.BadRequest(new { error = "The 'url' query parameter is required." });
    }

    try
    {
        var scrapeResult = await scraper.ScrapeAsync(url, limit, ct);
        if (scrapeResult.Total == 0)
        {
            return Results.NotFound(new { error = "No songs found to generate PDF.", url });
        }

        httpContext.Response.Headers["X-Sweet-Spot-Seconds"] = scrapeResult.SweetSpotSeconds.ToString("0.000");
        httpContext.Response.Headers["X-Requests-Per-Second"] = scrapeResult.RateMetrics.RequestsPerSecond.ToString("0.0");
        httpContext.Response.Headers["X-Rate-Limit-Hits"] = scrapeResult.RateMetrics.RateLimitHits.ToString();
        httpContext.Response.Headers["X-Total-Songs"] = scrapeResult.Total.ToString();

        var tempPdfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await Pdf.HtmlToPdf(scrapeResult.Html, tempPdfPath);

        var pdfBytes = await File.ReadAllBytesAsync(tempPdfPath, ct);
        try { File.Delete(tempPdfPath); } catch { }

        return Results.File(pdfBytes, "application/pdf", "cancionero.pdf");
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "PDF generation failed", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("GeneratePdfGet")
.WithTags("PDF");

app.MapPost("/pdf", async (HttpContext httpContext, [FromBody] PdfRequest request, [FromServices] Scraper scraper, CancellationToken ct) =>
{
    try
    {
        string htmlToRender;

        if (!string.IsNullOrWhiteSpace(request.Html))
        {
            htmlToRender = request.Html;
        }
        else if (!string.IsNullOrWhiteSpace(request.Url))
        {
            var scrapeResult = await scraper.ScrapeAsync(request.Url, request.Limit, ct);
            if (scrapeResult.Total == 0)
            {
                return Results.NotFound(new { error = "No songs found to generate PDF.", url = request.Url });
            }

            httpContext.Response.Headers["X-Sweet-Spot-Seconds"] = scrapeResult.SweetSpotSeconds.ToString("0.000");
            httpContext.Response.Headers["X-Requests-Per-Second"] = scrapeResult.RateMetrics.RequestsPerSecond.ToString("0.0");
            httpContext.Response.Headers["X-Rate-Limit-Hits"] = scrapeResult.RateMetrics.RateLimitHits.ToString();
            httpContext.Response.Headers["X-Total-Songs"] = scrapeResult.Total.ToString();

            htmlToRender = scrapeResult.Html;
        }
        else
        {
            return Results.BadRequest(new { error = "Provide either 'url' or 'html' in the request." });
        }

        var tempPdfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await Pdf.HtmlToPdf(htmlToRender, tempPdfPath);

        var pdfBytes = await File.ReadAllBytesAsync(tempPdfPath, ct);

        // If outputPath was requested, save to destination on disk as well
        var targetOutputPath = request.ResolvedOutputPath;
        string? savedFullPath = null;
        if (!string.IsNullOrWhiteSpace(targetOutputPath))
        {
            try
            {
                var hostHome = Environment.GetEnvironmentVariable("HOST_USER_HOME");
                var userHome = !string.IsNullOrWhiteSpace(hostHome)
                    ? hostHome
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (targetOutputPath.StartsWith("~/") || targetOutputPath.StartsWith("~\\"))
                {
                    targetOutputPath = Path.Combine(userHome, targetOutputPath.Substring(2));
                }

                savedFullPath = Path.GetFullPath(targetOutputPath);
                var dir = Path.GetDirectoryName(savedFullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllBytesAsync(savedFullPath, pdfBytes, ct);
                httpContext.Response.Headers["X-Saved-To"] = savedFullPath;
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Failed to write PDF to output path: {Path}", targetOutputPath);
            }
        }

        try
        {
            File.Delete(tempPdfPath);
        }
        catch
        {
            // Best effort cleanup
        }

        // If client requested application/json and specified output_path, return JSON acknowledgment
        if (httpContext.Request.Headers.Accept.Any(a => a?.Contains("application/json") == true) &&
            !string.IsNullOrWhiteSpace(savedFullPath))
        {
            return Results.Ok(new
            {
                status = "success",
                message = "PDF successfully generated and saved to disk.",
                output_path = savedFullPath,
                size_bytes = pdfBytes.Length
            });
        }

        return Results.File(pdfBytes, "application/pdf", "cancionero.pdf");
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "PDF generation failed", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("GeneratePdfPost")
.WithTags("PDF");

// Start local fetcher helper in background if available
try
{
    var nodeBin = Directory.Exists("/app/.playwright/node")
        ? Directory.GetFiles("/app/.playwright/node", "node", SearchOption.AllDirectories).FirstOrDefault()
        : null;

    nodeBin ??= "node";
    var fetcherPath = Path.Combine(AppContext.BaseDirectory, "fetcher.js");

    if (File.Exists(fetcherPath))
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = nodeBin,
            Arguments = $"\"{fetcherPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        var proc = System.Diagnostics.Process.Start(psi);
        if (proc != null)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { proc.Kill(true); } catch { }
            };
        }
    }
}
catch
{
    // Best effort helper startup
}

app.Run();
