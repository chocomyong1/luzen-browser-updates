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
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(120);
    private static readonly Regex ExplicitGemLevel = new(
        @"(?<!\d)(8|9|10)\s*(?:레벨|렙|lv|level)(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static MarketAdvice Evaluate(MarketBridgeRow row, DateTime receivedUtc, DateTime nowUtc)
    {
        if (receivedUtc == DateTime.MinValue || nowUtc - receivedUtc > MaxAge || receivedUtc > nowUtc.AddSeconds(15))
            return Wait("시세 갱신 대기");
        if (row.CompetitorHighest <= 0)
            return Wait("경쟁자 가격 미확인 · Gem Tracker v2.8.9.5 이상 필요");
        if (row.Distinct < 1 || row.Total < 1 || row.Distinct > row.Total ||
            row.HighCount > row.Total || row.CompetitorHighest > row.Highest)
            return Wait("경쟁 구매글 표본 불일치");
        if (row.Basis is null || row.Basis.Contains("미확인", StringComparison.Ordinal) ||
            row.MyPrice is not >= 1000)
            return Wait("내 추적 기준가 미확인");
        long step = row.Level switch { "8" or "9" => 100, "10" => 1000, _ => 0 };
        if (step == 0 || row.CompetitorHighest > 1_000_000_000 - step)
            return Wait("경쟁 구매가 범위 확인 필요");
        string confidence = $"경쟁 {row.Distinct}그룹";
        long basis = row.MyPrice.Value;
        long suggested = row.CompetitorHighest + step;
        if (suggested <= basis)
            return new(MarketAdviceKind.Hold, suggested,
                "경쟁가보다 높게 계산했지만 추적 기준가보다 낮음 · 디스코드 시세 확인 후 수동 판단", confidence);
        return new(MarketAdviceKind.Increase, suggested,
            $"확인된 경쟁 최고가 +{step:N0}원 · 디스코드 판매 시세·마진은 직접 확인", confidence);
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

    // Price recommendations are never applied automatically. Human approval
    // and existing per-listing site-ownership validation are still mandatory.
    internal static bool SafeBuyPriceIncrease(long savedPrice, long proposal) =>
        savedPrice >= 1000 && proposal > savedPrice && proposal % 10 == 0 &&
        proposal <= 1_000_000_000;

    private static MarketAdvice Wait(string reason) => new(MarketAdviceKind.Wait, null, reason, "—");
}
