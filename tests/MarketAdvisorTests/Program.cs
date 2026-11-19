using LuzenBrowserReplica;

DateTime now = DateTime.UtcNow;
MarketBridgeRow sample = new()
{
    Server = "카마인", Level = "10", MyPrice = 90000,
    Highest = 110000, CompetitorHighest = 110000,
    Median = 98000, NormalMax = 100000,
    Distinct = 12, Total = 16, HighCount = 1, Basis = "자동"
};

void Expect(MarketAdviceKind kind, long? expectedPrice, string scenario,
    MarketBridgeRow? item = null, DateTime? receivedUtc = null)
{
    var advice = MarketPriceAdvisor.Evaluate(item ?? sample, receivedUtc ?? now, now);
    if (advice.Kind != kind || advice.SuggestedPrice != expectedPrice)
        throw new Exception($"{scenario}: expected {kind} {expectedPrice}, got {advice}");
}

Expect(MarketAdviceKind.Increase, 111000, "Ten-level competitor + 1000");
Expect(MarketAdviceKind.Increase, 111000, "One competitor still counts",
    sample.WithValues(distinct: 1, total: 1, highCount: 0));
Expect(MarketAdviceKind.Wait, null, "Stale market", receivedUtc: now.AddSeconds(-121));
Expect(MarketAdviceKind.Wait, null, "Unknown own baseline", sample.WithValues(basis: "미확인", removePrice: true));
Expect(MarketAdviceKind.Wait, null, "Older tracker without owned-post exclusion",
    sample.WithValues(competitorHighest: 0));
Expect(MarketAdviceKind.Wait, null, "Invalid competitor above highest",
    sample.WithValues(competitorHighest: 120000));
Expect(MarketAdviceKind.Hold, 89000, "Manual Discord lowering must remain possible",
    sample.WithValues(competitorHighest: 88000));
Expect(MarketAdviceKind.Increase, 11900, "Eight-level competitor + 100",
    sample.WithValues(level: "8", competitorHighest: 11800, price: 10000));
Expect(MarketAdviceKind.Increase, 35200, "Nine-level competitor + 100",
    sample.WithValues(level: "9", competitorHighest: 35100, price: 30000));

if (!MarketPriceAdvisor.SafeBuyPriceIncrease(90000, 111000) ||
    MarketPriceAdvisor.SafeBuyPriceIncrease(112000, 111000) ||
    MarketPriceAdvisor.SafeBuyPriceIncrease(90000, 111001))
    throw new Exception("An unapproved decrease or malformed price passed the exact listing guard.");

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

Console.WriteLine("PASS exact competitor +100/+1000 advice, own-post version guard, stale check, and human approval");

// Test fixture cloning without changing MarketBridgeRow's mutable wire schema.
static class MarketRowTestExtensions
{
    internal static MarketBridgeRow WithValues(this MarketBridgeRow row,
        int? distinct = null, int? total = null, string? level = null,
        long? competitorHighest = null, long? highest = null,
        int? highCount = null, string? basis = null,
        long? price = null, bool removePrice = false)
    {
        return new MarketBridgeRow
        {
            Server = row.Server, Level = level ?? row.Level, Highest = highest ?? row.Highest,
            CompetitorHighest = competitorHighest ?? row.CompetitorHighest,
            Median = row.Median, NormalMax = row.NormalMax,
            MyPrice = removePrice ? null : price ?? row.MyPrice,
            Distinct = distinct ?? row.Distinct, Total = total ?? row.Total,
            HighCount = highCount ?? row.HighCount, Basis = basis ?? row.Basis
        };
    }
}
