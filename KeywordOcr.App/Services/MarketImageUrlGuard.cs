using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KeywordOcr.App.Services;

internal static class MarketImageUrlGuard
{
    private static readonly Regex ImgTagRegex = new(
        "<img\\b[^>]*\\bsrc=[\"']([^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageUrlRegex = new(
        @"https?://[^\s""'<>|,;]+?\.(?:jpg|jpeg|png|webp|bmp)(?:\?[^\s""'<>|,;]*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SafeFileNameRegex = new(
        @"^[A-Za-z0-9._%+\-=()]+$",
        RegexOptions.Compiled);

    public static bool IsAllowedUploadUrl(string? value)
    {
        var url = (value ?? "").Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasAsciiFileName(url);
    }

    public static string RemoveUnsafeImageTags(string? html)
    {
        var text = html ?? string.Empty;
        if (text.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            var urls = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in ImageUrlRegex.Matches(text))
            {
                var url = match.Value.Trim().Trim('"', '\'');
                if (IsAllowedUploadUrl(url) && seen.Add(url))
                    urls.Add(url);
            }

            if (urls.Count > 0)
                return "<center>" + string.Concat(urls.ConvertAll(url => $"<img src=\"{url}\">")) + "</center>";
        }

        return ImgTagRegex.Replace(
            text,
            match => IsAllowedUploadUrl(match.Groups[1].Value)
                ? $"<img src=\"{match.Groups[1].Value.Trim()}\">"
                : string.Empty);
    }

    private static bool HasAsciiFileName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath));
        if (string.IsNullOrWhiteSpace(fileName))
            return true;

        return SafeFileNameRegex.IsMatch(fileName);
    }
}
