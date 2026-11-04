namespace cifraclub_scraper_backend_csharp;

public static class Utils
{
    private const string BaseDomain = "https://cifraclub.com";

    public const string DefaultPrintCss = @"
@page {
    size: A4;
    margin: 15mm 12mm 15mm 15mm;
}
* {
    box-sizing: border-box;
}
body {
    margin: 0;
    padding: 0;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
    color: #212121;
    background: #fff;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
}
.cifra-song {
    page-break-before: always;
    break-before: page;
}
.cifra-song:first-child {
    page-break-before: avoid;
    break-before: avoid;
}
.cifra-header {
    margin-bottom: 14px;
    padding-bottom: 8px;
    border-bottom: 1px solid #e0e0e0;
}
.cifra-header h1 {
    font-size: 24px;
    font-weight: 700;
    color: #111111 !important;
    margin: 0 0 4px 0;
    line-height: 1.2;
}
.cifra-header h2 {
    font-size: 18px;
    font-weight: 700;
    color: #ff7700 !important;
    margin: 0 0 6px 0;
    line-height: 1.2;
}
.cifra-header small {
    font-size: 11px;
    color: #757575;
    display: block;
    margin: 0 0 4px 0;
}
.cifra-header a {
    text-decoration: none;
    color: inherit !important;
}
pre {
    font-family: 'Roboto Mono', 'Courier New', Courier, monospace !important;
    font-size: 12px !important;
    line-height: 1.5 !important;
    white-space: pre !important;
    word-wrap: normal !important;
    overflow: visible !important;
    margin: 0;
    padding: 0;
    border: 0;
    background: transparent;
    color: #212121;
}
pre b {
    color: #ff7700 !important;
    font-weight: 700 !important;
    font-family: 'Roboto Mono', 'Courier New', Courier, monospace !important;
    font-size: inherit !important;
}
.pages, .folha, .cifra_cnt {
    width: 100% !important;
    margin: 0 !important;
    padding: 0 !important;
    box-shadow: none !important;
    border: none !important;
}
.cifra_cnt b {
    color: #ff7700 !important;
    font-weight: 700 !important;
}
";

    public static string CreatePrintUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        string finalUrl = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : BaseDomain + (url.StartsWith('/') ? url : "/" + url);

        string paramsPart = string.Empty;
        if (finalUrl.Contains('#'))
        {
            var parts = finalUrl.Split('#', 2);
            finalUrl = parts[0];
            paramsPart = parts[1];
        }

        if (finalUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            finalUrl = finalUrl.Substring(0, finalUrl.Length - ".html".Length) + "/";
        }
        else if (!finalUrl.EndsWith('/'))
        {
            finalUrl += "/";
        }

        return finalUrl + "imprimir.html#footerChords=false" + (string.IsNullOrEmpty(paramsPart) ? string.Empty : "&" + paramsPart);
    }

    public static string RemoveTamA4(string html)
    {
        return html.Replace("tam_a4", string.Empty);
    }

    public static string GenerateHtml(IEnumerable<SongDto> songs)
    {
        return GenerateHtml(songs.Select(s => s.Content).ToArray());
    }

    public static string GenerateHtml(string[] contents)
    {
        return $@"<!DOCTYPE html>
<html>
    <head>
        <meta charset=""utf-8"">
        <link href=""https://akamai.sscdn.co/cc/css/0830d.cifra_print.css"" media=""all"" rel=""stylesheet"" type=""text/css""/>
        <style>
{DefaultPrintCss}
        </style>
    </head>
    <body>
        {RemoveTamA4(string.Join("\n", contents))}
    </body>
</html>";
    }
}
