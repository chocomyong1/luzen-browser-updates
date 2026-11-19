using System;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

internal enum MarketAdviceKind { Wait, Hold, Increase }

internal readonly record struct MarketAdvice(
    MarketAdviceKind Kind, long? SuggestedPrice, string Reason, string Confidence);

// A deliberately conservative, explainable recommendation. It does not
// mutate a listing, inspect account credentials, or submit anything to a site.
internal static class MarketPriceAdvisor
{
    private const int MinimumIndependentGroups = 5;
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(120);
    private static readonly Regex ExplicitGemLevel = new(
        @"(?<!\d)(8|9|10)\s*(?:레벨|렙|lv|level)(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static MarketAdvice Evaluate(MarketBridgeRow row, DateTime receivedUtc, DateTime nowUtc)
    {
        if (receivedUtc == DateTime.MinValue || nowUtc - receivedUtc > MaxAge || receivedUtc > nowUtc.AddSeconds(15))
            return Wait("시세 갱신 대기");
        if (row.Distinct < MinimumIndependentGroups || row.Total < MinimumIndependentGroups ||
            row.Distinct > row.Total || row.HighCount > row.Total)
            return Wait("독립 표본 5그룹 미만 또는 표본 불일치");
        if (row.Basis is null || row.Basis.Contains("미확인", StringComparison.Ordinal) ||
            row.MyPrice is not >= 1000)
            return Wait("내 추적 기준가 미확인");
        if (row.Median < 1000 || row.NormalMax is not >= 1000)
            return Wait("검증된 일반가 미확인");

        string confidence = row.Distinct >= 10 && row.Total >= 10 ? "높음" : "보통";
        long basis = row.MyPrice.Value;
        // Anchor on the independent median. For 8+ independent groups, count
        // only 25% of a bounded (<=10%) gap to the outlier-filtered maximum;
        // an isolated high buyer must never become our full asking price.
        long ordinaryCeiling = Math.Min(row.NormalMax.Value, row.Median + row.Median / 10);
        long regularPremium = row.Distinct >= 8
            ? Math.Max(0, ordinaryCeiling - row.Median) / 4 : 0;
        long marketCeiling = RoundDown100(Math.Min(row.NormalMax.Value, row.Median + regularPremium));
        long maxIncrease = RoundDown100(basis + basis / 20); // <=5% per approval.
        long suggested = Math.Min(marketCeiling, maxIncrease);
        long meaningful = Math.Max(500, RoundUp100((basis + 99) / 100)); // ~1% or 500 won.
        if (suggested <= basis || suggested - basis < meaningful)
            return new(MarketAdviceKind.Hold, basis,
                "중앙값·일반 최고가 기준 인상 근거 부족", confidence);
        return new(MarketAdviceKind.Increase, suggested,
            "중앙값 기반 · 일반 최고가 차이 일부만 반영 · 1회 인상 5% 이내", confidence);
    }

    internal static bool MatchesBuyListing(MarketBridgeRow market, string? game,
        string? tradeSide, string? server, string? title)
    {
        if (game?.Contains("로스트아크", StringComparison.OrdinalIgnoreCase) != true ||
            tradeSide != "구매" ||
            !string.Equals(server?.Trim(), market.Server.Trim(), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(title)) return false;
        var levels = ExplicitGemLevel.Matches(title);
        if (levels.Count != 1) return false; // Mixed or ambiguous item levels are never auto-selected.
        return levels[0].Groups[1].Value == market.Level;
    }

    private static long RoundDown100(long value) => value / 100 * 100;
    private static long RoundUp100(long value) => (value + 99) / 100 * 100;
    private static MarketAdvice Wait(string reason) => new(MarketAdviceKind.Wait, null, reason, "—");
}
