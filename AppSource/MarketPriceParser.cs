using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

// External reference ONLY. These are observed asking prices, never a verified
// ItemMania execution price or an input to automatic listing submission.
internal sealed record MarketPriceQuote(string Server, int? ItemManiaPer100k, int? ItemBayPer100k);

internal static class MarketPriceParser
{
    internal const string SourceUrl = "https://gemdex.kr/markets?game=lost-ark";
    private static readonly string[] LostArkServers =
    {
        "아브렐슈드", "카제로스", "실리안", "루페온", "카마인", "니나브", "카단", "아만"
    };

    public static IReadOnlyDictionary<string, MarketPriceQuote> Parse(string html)
    {
        var result = new Dictionary<string, MarketPriceQuote>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html)) return result;

        // Read the website's *visible table cells*, not embedded scripts or
        // unrelated server names appearing in charts and navigation.
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>(?<body>.*?)</tr>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2)))
        {
            var cells = Regex.Matches(row.Groups["body"].Value,
                @"<t[dh]\b[^>]*>(?<value>.*?)</t[dh]>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
            // The public comparison table columns are:
            // favourite | server | median | 24-hour change | ItemMania | ItemBay | ...
            if (cells.Count < 6) continue;
            var serverText = VisibleText(cells[1].Groups["value"].Value);
            var server = LostArkServers.FirstOrDefault(name =>
                serverText.StartsWith(name, StringComparison.Ordinal) &&
                (serverText.Length == name.Length ||
                 !char.IsLetter(serverText[name.Length])));
            if (server is null) continue;

            var mania = WonPer100k(VisibleText(cells[4].Groups["value"].Value));
            var bay = WonPer100k(VisibleText(cells[5].Groups["value"].Value));
            if (mania is null && bay is null) continue;
            result[server] = new MarketPriceQuote(server, mania, bay);
        }
        return result;
    }

    private static string VisibleText(string source)
    {
        // Ignore unrelated strings in attributes/scripts when reading cells.
        var noTags = Regex.Replace(source, @"<[^>]*>", " ", RegexOptions.Singleline);
        return Regex.Replace(WebUtility.HtmlDecode(noTags), @"\s+", " ").Trim();
    }

    private static int? WonPer100k(string text)
    {
        var match = Regex.Match(text, @"(?<!\d)(?<number>\d{1,3}(?:,\d{3})*|\d+)\s*원");
        if (!match.Success || !int.TryParse(match.Groups["number"].Value.Replace(",", ""), out int value))
            return null;
        // GEMDEX quotes Lost Ark at 10,000 gold; the original Luzen UI uses
        // 100,000 gold. Never confuse the two units.
        return value > 0 && value <= 100_000 ? checked(value * 10) : null;
    }
}
