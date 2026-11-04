using System.Text.Json.Serialization;

namespace cifraclub_scraper_backend_csharp;

public record SongDto(
    string Title,
    string Artist,
    string Url,
    string PrintUrl,
    string Content
);

public record RateMetrics(
    [property: JsonPropertyName("sweet_spot_seconds")] double SweetSpotSeconds,
    [property: JsonPropertyName("sweet_spot_ms")] double SweetSpotMs,
    [property: JsonPropertyName("requests_per_second")] double RequestsPerSecond,
    [property: JsonPropertyName("rate_limit_hits")] int RateLimitHits,
    [property: JsonPropertyName("total_requests")] int TotalRequests,
    [property: JsonPropertyName("total_duration_seconds")] double TotalDurationSeconds
);

public record ScrapeResponse(
    string Url,
    string? Artist,
    int Total,
    [property: JsonPropertyName("sweet_spot_seconds")] double SweetSpotSeconds,
    [property: JsonPropertyName("rate_metrics")] RateMetrics RateMetrics,
    List<SongDto> Songs,
    string Html
);

public record ScrapeRequest(
    string Url,
    int? Limit = null
);

public record PdfRequest(
    string? Url = null,
    string? Html = null,
    int? Limit = null,
    string? OutputPath = null,
    [property: JsonPropertyName("output_path")]
    string? OutputPathSnake = null
)
{
    public string? ResolvedOutputPath => !string.IsNullOrWhiteSpace(OutputPath) ? OutputPath : OutputPathSnake;
};
