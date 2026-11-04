using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;

namespace cifraclub_scraper_backend_csharp;

public class Scraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Scraper> _logger;

    private static readonly HashSet<string> ReservedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "artistas", "musico", "explorar", "estilos", "top", "mais-acessadas",
        "contato", "termos", "privacidade", "blog", "noticias", "apostilas"
    };

    private static readonly HashSet<string> ReservedSubPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "discografia", "fotos", "letras", "videos", "biografia", "tabs", "simplificada"
    };

    private static readonly Regex SongbookItemRegex = new(
        @"\\?""song\\""?:\{[^{}]*\\?""slug\\""?:\\?""([^""\\]+)\\""?[^{}]*\}(?:[^{}]*\{[^{}]*\}){0,2}[^{}]*\\?""artist\\""?:\{[^{}]*\\?""slug\\""?:\\?""([^""\\]+)\\""?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex SongbookItemArtistFirstRegex = new(
        @"\\?""artist\\""?:\{[^{}]*\\?""slug\\""?:\\?""([^""\\]+)\\""?[^{}]*\}(?:[^{}]*\{[^{}]*\}){0,2}[^{}]*\\?""song\\""?:\{[^{}]*\\?""slug\\""?:\\?""([^""\\]+)\\""?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex SongbookNameRegex = new(
        @"\\?""type\\""?:\\?""[^""\\]+\\""?,\\?""name\\""?:\\?""([^""\\]+)\\""\}\s*,\s*\\?""isPrivate\\""?:(?:true|false)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private record DiscoveryResult(List<string> Urls, string? CollectionName);

    public Scraper(HttpClient httpClient, ILogger<Scraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private Task<string?> GetHtmlAsync(string url, CancellationToken ct = default) =>
        GetHtmlAsync(url, null, ct);

    private async Task<string?> GetHtmlAsync(string url, AdaptiveRateGovernor? governor, CancellationToken ct = default)
    {
        var cleanUrl = url.Split('#')[0];

        for (int attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                if (governor != null)
                {
                    await governor.WaitTurnAsync(ct);
                }

                using var response = await _httpClient.GetAsync(cleanUrl, ct);

                // Handle rate limit (429) on direct fetch
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (governor != null)
                    {
                        governor.ReportRateLimit(cooldownSeconds: 4.0);
                    }
                    else
                    {
                        var backoffSec = Math.Min(20, attempt * 3);
                        _logger.LogWarning("Rate limit (429) hit for {Url} (attempt {Attempt}). Backing off {Sec}s...", cleanUrl, attempt, backoffSec);
                        await Task.Delay(TimeSpan.FromSeconds(backoffSec), ct);
                    }
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    governor?.ReportSuccess();
                    return await response.Content.ReadAsStringAsync(ct);
                }

                // If direct fetch was blocked, rate-limited or failed, fallback to local fetcher helper
                _logger.LogWarning("Direct fetch returned {StatusCode} for {Url} (attempt {Attempt}). Attempting fallback fetcher...", response.StatusCode, cleanUrl, attempt);
                var fallbackUrl = $"http://127.0.0.1:5001/?url={Uri.EscapeDataString(cleanUrl)}";
                using var fallbackReq = new HttpRequestMessage(HttpMethod.Get, fallbackUrl)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var fallbackRes = await _httpClient.SendAsync(fallbackReq, ct);

                // Handle rate limit (429) on fallback fetch
                if (fallbackRes.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (governor != null)
                    {
                        governor.ReportRateLimit(cooldownSeconds: 4.0);
                    }
                    else
                    {
                        var backoffSec = Math.Min(20, attempt * 3);
                        _logger.LogWarning("Fallback rate limit (429) hit for {Url} (attempt {Attempt}). Backing off {Sec}s...", cleanUrl, attempt, backoffSec);
                        await Task.Delay(TimeSpan.FromSeconds(backoffSec), ct);
                    }
                    continue;
                }

                if (fallbackRes.IsSuccessStatusCode)
                {
                    governor?.ReportSuccess();
                    return await fallbackRes.Content.ReadAsStringAsync(ct);
                }

                // Definite 404
                if (response.StatusCode == HttpStatusCode.NotFound && fallbackRes.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("URL not found (404): {Url}", cleanUrl);
                    return null;
                }

                _logger.LogWarning("Fetch {Url} failed with status {StatusCode} (fallback {FallbackStatus})", cleanUrl, response.StatusCode, fallbackRes.StatusCode);
                
                if (attempt >= 6)
                {
                    return null;
                }

                await Task.Delay(500, ct);
            }
            catch (Exception ex) when (attempt < 6)
            {
                _logger.LogWarning(ex, "Transient error fetching {Url}. Retrying in 500ms...", cleanUrl);
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching {Url}", cleanUrl);
                return null;
            }
        }

        return null;
    }

    private Task<HtmlDocument?> GetHtmlDocumentAsync(string url, CancellationToken ct = default) =>
        GetHtmlDocumentAsync(url, null, ct);

    private async Task<HtmlDocument?> GetHtmlDocumentAsync(string url, AdaptiveRateGovernor? governor, CancellationToken ct = default)
    {
        var content = await GetHtmlAsync(url, governor, ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(content);
        return doc;
    }

    private static Uri NormalizeUri(string inputUrl)
    {
        var trimmed = inputUrl.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return new Uri(trimmed);
    }

    public async Task<List<string>> GetSongUrlsAsync(string rawUrl, int? limit = null, CancellationToken ct = default)
    {
        var discovery = await DiscoverUrlsAsync(rawUrl, limit, ct);
        return discovery.Urls;
    }

    private async Task<DiscoveryResult> DiscoverUrlsAsync(string rawUrl, int? limit = null, CancellationToken ct = default)
    {
        var baseUri = NormalizeUri(rawUrl);
        var rawPath = baseUri.AbsolutePath.Trim('/');
        var segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Case 1: If it is already a direct song URL (e.g. /artist/song/ or ends with imprimir.html)
        // Guard against user profiles, repertoires and playlists
        var isRepertoireOrProfile = segments.Length > 0 &&
            (segments[0].StartsWith('@') ||
             segments[0].Equals("musico", StringComparison.OrdinalIgnoreCase) ||
             rawPath.Contains("repertorio", StringComparison.OrdinalIgnoreCase) ||
             rawPath.Contains("playlist", StringComparison.OrdinalIgnoreCase));

        if (!isRepertoireOrProfile && segments.Length >= 2 && !ReservedSegments.Contains(segments[0]))
        {
            if (rawPath.EndsWith("imprimir.html", StringComparison.OrdinalIgnoreCase) ||
                (segments.Length == 2 && !ReservedSubPaths.Contains(segments[1])))
            {
                var cleanSongUrl = baseUri.GetLeftPart(UriPartial.Path);
                if (cleanSongUrl.EndsWith("imprimir.html", StringComparison.OrdinalIgnoreCase))
                {
                    cleanSongUrl = cleanSongUrl.Substring(0, cleanSongUrl.Length - "imprimir.html".Length);
                }
                return new DiscoveryResult(new List<string> { cleanSongUrl }, null);
            }
        }

        var htmlContent = await GetHtmlAsync(baseUri.ToString(), ct);
        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            return new DiscoveryResult(new List<string>(), null);
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddUrl(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            if (limit.HasValue && results.Count >= limit.Value) return;

            var absolute = candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : new Uri(baseUri, candidate).ToString();

            // Normalize song url (strip imprimir.html if any)
            if (absolute.EndsWith("imprimir.html", StringComparison.OrdinalIgnoreCase))
            {
                absolute = absolute.Substring(0, absolute.Length - "imprimir.html".Length);
            }

            if (!absolute.EndsWith('/'))
            {
                absolute += "/";
            }

            if (seen.Add(absolute))
            {
                results.Add(absolute);
            }
        }

        string? collectionName = null;

        // Case 2: Check for embedded songbook / playlist / Next.js JSON items in the HTML
        var songbookMatches = SongbookItemRegex.Matches(htmlContent);
        if (songbookMatches.Count > 0)
        {
            var nameMatch = SongbookNameRegex.Match(htmlContent);
            if (nameMatch.Success)
            {
                collectionName = nameMatch.Groups[1].Value;
            }

            foreach (Match m in songbookMatches)
            {
                if (limit.HasValue && results.Count >= limit.Value) break;
                var songSlug = m.Groups[1].Value;
                var artistSlugFound = m.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(songSlug) && !string.IsNullOrWhiteSpace(artistSlugFound))
                {
                    AddUrl($"https://{baseUri.Host}/{artistSlugFound}/{songSlug}/");
                }
            }

            if (results.Count > 0)
            {
                return new DiscoveryResult(results, collectionName);
            }
        }

        var artistFirstMatches = SongbookItemArtistFirstRegex.Matches(htmlContent);
        if (artistFirstMatches.Count > 0)
        {
            var nameMatch = SongbookNameRegex.Match(htmlContent);
            if (nameMatch.Success)
            {
                collectionName = nameMatch.Groups[1].Value;
            }

            foreach (Match m in artistFirstMatches)
            {
                if (limit.HasValue && results.Count >= limit.Value) break;
                var artistSlugFound = m.Groups[1].Value;
                var songSlug = m.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(songSlug) && !string.IsNullOrWhiteSpace(artistSlugFound))
                {
                    AddUrl($"https://{baseUri.Host}/{artistSlugFound}/{songSlug}/");
                }
            }

            if (results.Count > 0)
            {
                return new DiscoveryResult(results, collectionName);
            }
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        // Case 3: Artist page - check for "explorar/musicas/?artist=" link to paginate through ALL songs
        var isArtistPage = segments.Length == 1 &&
                           !segments[0].StartsWith('@') &&
                           !ReservedSegments.Contains(segments[0]);
        var artistSlug = isArtistPage ? segments[0] : null;

        if (isArtistPage)
        {
            var exploreNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'explorar/musicas') and contains(@href, 'artist=')]");
            if (exploreNode != null)
            {
                var exploreHref = WebUtility.HtmlDecode(exploreNode.GetAttributeValue("href", ""));
                var exploreUri = new Uri(baseUri, exploreHref);
                var query = HttpUtility.ParseQueryString(exploreUri.Query);
                var artistId = query["artist"];

                if (!string.IsNullOrEmpty(artistId))
                {
                    var page = 1;
                    while (!ct.IsCancellationRequested)
                    {
                        var pageUrl = $"https://{baseUri.Host}/explorar/musicas/?artist={artistId}&page={page}";
                        var pageDoc = await GetHtmlDocumentAsync(pageUrl, ct);
                        if (pageDoc == null) break;

                        var countBefore = results.Count;
                        ExtractSongLinks(pageDoc, baseUri, artistSlug, AddUrl, limit, () => results.Count);

                        if (results.Count == countBefore || (limit.HasValue && results.Count >= limit.Value))
                        {
                            break;
                        }

                        page++;
                        if (page > 50) break; // Safety limit
                    }
                }
            }
        }

        // Case 4: If no explore link or no songs gathered yet, extract directly from current document
        if (results.Count == 0)
        {
            ExtractSongLinks(doc, baseUri, artistSlug, AddUrl, limit, () => results.Count);
        }

        // Case 5: Playlists, Repertoires, User lists in traditional DOM (e.g. #js-mod-read-list or ol.list-links)
        if (results.Count == 0)
        {
            var listNodes = doc.DocumentNode.SelectNodes("//ol[contains(@class,'list')]//a | //ul[contains(@class,'list')]//a | //*[@id='js-mod-read-list']//a");
            if (listNodes != null)
            {
                foreach (var a in listNodes)
                {
                    if (limit.HasValue && results.Count >= limit.Value) break;
                    var href = WebUtility.HtmlDecode(a.GetAttributeValue("href", ""));
                    if (IsValidSongHref(href, artistSlug))
                    {
                        AddUrl(href);
                    }
                }
            }
        }

        return new DiscoveryResult(results, collectionName);
    }

    private static void ExtractSongLinks(HtmlDocument doc, Uri baseUri, string? artistSlug, Action<string> addUrl, int? limit, Func<int> currentCount)
    {
        var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
        if (allLinks == null) return;

        foreach (var a in allLinks)
        {
            if (limit.HasValue && currentCount() >= limit.Value) break;
            var href = WebUtility.HtmlDecode(a.GetAttributeValue("href", ""));
            if (IsValidSongHref(href, artistSlug))
            {
                addUrl(href);
            }
        }
    }

    private static bool IsValidSongHref(string href, string? artistSlug)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;

        var clean = href.Split('?', '#')[0].Trim('/');
        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2) return false;

        if (ReservedSegments.Contains(parts[0])) return false;
        if (ReservedSubPaths.Contains(parts[1])) return false;

        if (!string.IsNullOrEmpty(artistSlug) && !parts[0].Equals(artistSlug, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public Task<SongDto?> ScrapeSongAsync(string songUrl, CancellationToken ct = default) =>
        ScrapeSongAsync(songUrl, null, ct);

    public async Task<SongDto?> ScrapeSongAsync(string songUrl, AdaptiveRateGovernor? governor, CancellationToken ct = default)
    {
        var printUrl = Utils.CreatePrintUrl(songUrl);
        var doc = await GetHtmlDocumentAsync(printUrl, governor, ct);

        // Fallback to original URL if printUrl failed or returned no pre
        if (doc == null || doc.DocumentNode.SelectSingleNode("//pre") == null)
        {
            doc = await GetHtmlDocumentAsync(songUrl, governor, ct);
        }

        if (doc == null)
        {
            return null;
        }

        // Clean out images, scripts, ads, and screen-reader elements
        var removeNodes = doc.DocumentNode.SelectNodes("//img | //script | //style | //iframe | //aside | //*[contains(@class, 'u-srOnly')]");
        if (removeNodes != null)
        {
            foreach (var node in removeNodes)
            {
                node.Remove();
            }
        }

        // Extract Title
        var titleNode = doc.DocumentNode.SelectSingleNode("//header//h1") ??
                        doc.DocumentNode.SelectSingleNode("//h1");
        var title = titleNode != null
            ? WebUtility.HtmlDecode(titleNode.InnerText).Trim()
            : string.Empty;

        // Extract Artist
        var artistNode = doc.DocumentNode.SelectSingleNode("//header//h2") ??
                         doc.DocumentNode.SelectSingleNode("//h2") ??
                         doc.DocumentNode.SelectSingleNode("//small");
        var artist = artistNode != null
            ? WebUtility.HtmlDecode(artistNode.InnerText).Trim()
            : string.Empty;

        if (artist.Equals("Menú principal", StringComparison.OrdinalIgnoreCase) ||
            artist.StartsWith("Menú de", StringComparison.OrdinalIgnoreCase))
        {
            artist = string.Empty;
        }

        // Fallback title/artist from URL if empty
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            var uri = new Uri(songUrl);
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (string.IsNullOrWhiteSpace(artist)) artist = FormatSlug(parts[0]);
                if (string.IsNullOrWhiteSpace(title)) title = FormatSlug(parts[1]);
            }
        }

        // Extract Composer
        var composerNode = doc.DocumentNode.SelectSingleNode("//header//small") ??
                           doc.DocumentNode.SelectSingleNode("//small");
        var composer = composerNode != null
            ? WebUtility.HtmlDecode(composerNode.InnerText).Trim()
            : string.Empty;

        // Clean pre element (strip inline styles that break monospace sizing)
        var preNode = doc.DocumentNode.SelectSingleNode("//pre");
        if (preNode != null)
        {
            preNode.Attributes.Remove("style");
        }
        var preHtml = preNode?.OuterHtml ?? string.Empty;

        // Build clean, styled semantic song section with explicit page breaks
        var composerHtml = !string.IsNullOrWhiteSpace(composer)
            ? $"<small>{WebUtility.HtmlEncode(composer)}</small>"
            : string.Empty;

        var content = $@"<section class=""cifra-song"">
    <header class=""cifra-header"">
        <h1>{WebUtility.HtmlEncode(title)}</h1>
        <h2>{WebUtility.HtmlEncode(artist)}</h2>
        {composerHtml}
    </header>
    <div class=""cifra-body"">
        {preHtml}
    </div>
</section>";

        return new SongDto(title, artist, songUrl, printUrl, content);
    }

    private static string FormatSlug(string slug)
    {
        var words = slug.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
    }

    public async Task<ScrapeResponse> ScrapeAsync(string rawUrl, int? limit = null, CancellationToken ct = default)
    {
        var discovery = await DiscoverUrlsAsync(rawUrl, limit, ct);
        var songUrls = discovery.Urls;

        if (songUrls.Count == 0)
        {
            var emptyMetrics = new RateMetrics(0, 0, 0, 0, 0, 0);
            return new ScrapeResponse(rawUrl, null, 0, 0, emptyMetrics, new List<SongDto>(), string.Empty);
        }

        var governor = new AdaptiveRateGovernor(initialDelayMs: 200, minDelayMs: 80, maxDelayMs: 3000);
        var sw = Stopwatch.StartNew();
        var songsMap = new ConcurrentDictionary<string, SongDto>();

        await Parallel.ForEachAsync(songUrls, new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        }, async (url, token) =>
        {
            var song = await ScrapeSongAsync(url, governor, token);
            if (song != null)
            {
                songsMap[url] = song;
            }
        });

        sw.Stop();

        // Safety sweep: retry any missed songs to guarantee zero dropped songs
        var missedUrls = songUrls.Where(u => !songsMap.ContainsKey(u)).ToList();
        if (missedUrls.Count > 0)
        {
            _logger.LogWarning("Safety sweep: retrying {Count} missed songs...", missedUrls.Count);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            foreach (var missedUrl in missedUrls)
            {
                var song = await ScrapeSongAsync(missedUrl, governor, ct);
                if (song != null)
                {
                    songsMap[missedUrl] = song;
                }
            }
        }

        var metrics = governor.GetMetrics(sw.Elapsed);

        // Maintain original order of URLs
        var songs = songUrls
            .Where(songsMap.ContainsKey)
            .Select(u => songsMap[u])
            .ToList();

        var uniqueArtists = songs
            .Select(s => s.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? artistName;
        string collectionTitle;

        if (uniqueArtists.Count == 1)
        {
            artistName = uniqueArtists[0];
            collectionTitle = $"Cancionero - {artistName}";
        }
        else if (!string.IsNullOrWhiteSpace(discovery.CollectionName))
        {
            artistName = $"Repertorio: {discovery.CollectionName}";
            collectionTitle = $"Cancionero - {discovery.CollectionName}";
        }
        else if (uniqueArtists.Count > 1)
        {
            artistName = "Varios Artistas";
            collectionTitle = "Cancionero Cifra Club";
        }
        else
        {
            artistName = null;
            collectionTitle = "Cancionero Cifra Club";
        }

        var html = Utils.GenerateHtml(songs);

        return new ScrapeResponse(rawUrl, artistName, songs.Count, metrics.SweetSpotSeconds, metrics, songs, html);
    }
}
