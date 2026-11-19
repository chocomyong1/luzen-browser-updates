using LuzenBrowserReplica;

DateTime now = DateTime.UtcNow;
MarketBridgeRow sample = new()
{
    Server = "카마인", Level = "10", MyPrice = 90000,
    Highest = 110000, Median = 98000, NormalMax = 100000,
    Distinct = 12, Total = 16, HighCount = 1, Basis = "자동"
};

void Expect(MarketAdviceKind kind, long? expectedPrice, string scenario,
    MarketBridgeRow? item = null, DateTime? receivedUtc = null)
{
    var advice = MarketPriceAdvisor.Evaluate(item ?? sample, receivedUtc ?? now, now);
    if (advice.Kind != kind || advice.SuggestedPrice != expectedPrice)
        throw new Exception($"{scenario}: expected {kind} {expectedPrice}, got {advice}");
}

Expect(MarketAdviceKind.Increase, 94500, "Capped increase despite anomalous highest");
Expect(MarketAdviceKind.Wait, null, "Sparse market", sample.WithValues(distinct: 3, total: 3));
Expect(MarketAdviceKind.Wait, null, "Stale market", receivedUtc: now.AddSeconds(-121));
Expect(MarketAdviceKind.Wait, null, "Unknown own baseline", sample.WithValues(basis: "미확인", removePrice: true));
Expect(MarketAdviceKind.Wait, null, "No normal maximum", sample.WithValues(removeNormal: true));
Expect(MarketAdviceKind.Hold, 90000, "Median below own price", sample.WithValues(median: 88000, normal: 100000));
Expect(MarketAdviceKind.Increase, 92200, "One high bid has capped influence",
    sample.WithValues(median: 90000, normal: 110000));
Expect(MarketAdviceKind.Hold, 90000, "Sparse independent groups cannot follow one high bid",
    sample.WithValues(median: 90000, normal: 110000, distinct: 6));
Expect(MarketAdviceKind.Increase, 91200, "Small but meaningful signal",
    sample.WithValues(median: 91000, normal: 92000));
Expect(MarketAdviceKind.Hold, 90000, "Ignore small gaps below 1 percent",
    sample.WithValues(median: 90300, normal: 91000));

if (!MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "10 10 10 레벨 겁화 구매합니다") ||
    !MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "10레벨 작열 삽니다") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "판매", "카마인", "10레벨") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "메이플스토리", "구매", "카마인", "10레벨") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "루페온", "10레벨") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "9레벨") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "9레벨 10레벨") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "겁화 작열 구매") ||
    MarketPriceAdvisor.MatchesBuyListing(sample, "로스트아크", "구매", "카마인", "10 10 10 구매"))
    throw new Exception("An unrelated or ambiguously leveled listing matched the recommendation.");

Console.WriteLine("PASS conservative market recommendations, stale/sparse guards, and exact buy listing matching");

// Test fixture cloning without changing MarketBridgeRow's mutable wire schema.
static class MarketRowTestExtensions
{
    internal static MarketBridgeRow WithValues(this MarketBridgeRow row,
        int? distinct = null, int? total = null, long? median = null,
        long? normal = default, string? basis = null,
        bool removeNormal = false, bool removePrice = false)
    {
        return new MarketBridgeRow
        {
            Server = row.Server, Level = row.Level, Highest = row.Highest,
            Median = median ?? row.Median,
            NormalMax = removeNormal ? null : normal ?? row.NormalMax,
            MyPrice = removePrice ? null : row.MyPrice,
            Distinct = distinct ?? row.Distinct, Total = total ?? row.Total,
            HighCount = row.HighCount, Basis = basis ?? row.Basis
        };
    }
}
