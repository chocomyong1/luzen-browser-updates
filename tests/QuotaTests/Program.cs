using LuzenBrowserReplica;

namespace LuzenBrowserReplica;

internal sealed class ListingJob
{
    public string SiteId { get; set; } = "";
    public string Game { get; set; } = "";
    public string Server { get; set; } = "";
}

internal static class Program
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Hourly quota test failed: " + message);
    }

    private static void Main()
    {
        string notice = "19시까지 해당 ID로 17개 / IP에서 27개까지 추가등록 가능합니다. (추가등록 가능한 횟수에는 재등록도 포함됩니다.)";
        Check(HourlyServerQuota.ParseIdRemaining(notice) == 17, "parse ID, ignore IP");
        Check(HourlyServerQuota.ParseIdRemaining("IP에서 27개까지 추가등록 가능합니다") is null, "never use IP count");
        Check(HourlyServerQuota.ParseIdRemaining("10레벨 보석 17개") is null, "do not invent remaining quota");

        DateTime start = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc); // 18:00 KST
        var counters = new Dictionary<string, HourlyServerCounter>();
        var kadan = new ListingJob { SiteId = "id1", Game = "로스트아크", Server = "카단" };
        var karmine = new ListingJob { SiteId = "id1", Game = "로스트아크", Server = "카마인" };
        var anotherId = new ListingJob { SiteId = "id2", Game = "로스트아크", Server = "카단" };
        var a = HourlyServerQuota.Current(counters, kadan, start);
        var b = HourlyServerQuota.Current(counters, karmine, start);
        var c = HourlyServerQuota.Current(counters, anotherId, start);
        for (int i = 0; i < 20; i++) HourlyServerQuota.MarkAttempt(a);
        HourlyServerQuota.MarkAttempt(b);
        Check(HourlyServerQuota.Remaining(a, 60) == 40, "Kadan 20 -> 40");
        Check(HourlyServerQuota.Remaining(b, 60) == 59, "Karmine 1 -> 59");
        Check(HourlyServerQuota.Remaining(c, 60) == 60, "another ID unaffected");

        Check(HourlyServerQuota.Reconcile(a, 60, 39) is not null, "report mismatch");
        Check(HourlyServerQuota.Remaining(a, 60) == 39, "use smaller actual quota");
        HourlyServerQuota.MarkAttempt(a);
        Check(HourlyServerQuota.Remaining(a, 60) == 38, "site quota decremented on submit");
        Check(HourlyServerQuota.Reconcile(a, 60, 38) is null, "matching report no alert");

        var d = HourlyServerQuota.Current(counters, karmine, start);
        DateTime next = HourlyServerQuota.NextAllowedUtc(start, start.AddSeconds(1), d, 60, 21);
        Check(next >= start.AddSeconds(21) && next <= start.AddSeconds(70), "21s floor plus hourly spread");
        var higher = HourlyServerQuota.Current(counters, anotherId, start);
        HourlyServerQuota.MarkAttempt(higher);
        Check(HourlyServerQuota.Reconcile(higher, 30, 59) is not null, "first higher quota alert");
        HourlyServerQuota.MarkAttempt(higher);
        Check(HourlyServerQuota.Reconcile(higher, 30, 58) is null, "no duplicate every post");

        DateTime reset = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc); // 19:00 KST
        Check(HourlyServerQuota.Current(counters, kadan, reset).Attempts == 0, "resets on Korean hour");
        Check(HourlyServerQuota.Remaining(HourlyServerQuota.Current(counters, kadan, reset), 60) == 60,
            "fresh hourly allocation");
        Console.WriteLine("PASS ID/server independent hourly quotas, actual site reconciliation, pacing, Telegram dedup");
    }
}
