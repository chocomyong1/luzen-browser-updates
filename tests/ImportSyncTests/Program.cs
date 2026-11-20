using LuzenBrowserReplica;

namespace LuzenBrowserReplica;

internal sealed class ListingJob
{
    public string Id { get; set; } = "id";
    public string SiteId { get; set; } = "";
    public string SubId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string OriginalTradeSide { get; set; } = "";
    public string ListingNumber { get; set; } = "";
    public bool ImportedFromSite { get; set; }
    public DateTime ImportedUtc { get; set; }
    public string Game { get; set; } = "";
    public string Server { get; set; } = "";
    public string Title { get; set; } = "";
    public string SiteObservedTitle { get; set; } = "";
    public string ItemCategory { get; set; } = "";
    public string TradingMethod { get; set; } = "";
    public string MinQuantity { get; set; } = "";
    public string MaxQuantity { get; set; } = "";
    public string Character { get; set; } = "";
    public string Description { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Price { get; set; } = "";
    public string PendingSitePriceTarget { get; set; } = "";
    public string VerifiedSitePrice { get; set; } = "";
    public DateTime? VerifiedSitePriceUtc { get; set; }
    public string EntryUrl { get; set; } = "";
    public string SourceListUrl { get; set; } = "";
    public bool Enabled { get; set; }
    public bool AutoRepost { get; set; }
    public bool InitialCompleted { get; set; }
    public bool RequiresReview { get; set; }
    public DateTime NextDueUtc { get; set; }
    public string LastStatus { get; set; } = "";
    public DateTime? PendingRepostAttemptUtc { get; set; }
}

internal static class GameServerCatalog
{
    public static string? CanonicalServer(string game, string server) => server;
}

internal static class Program
{
    private const string Number = "2026070508123456";
    private const string Title = "9레벨 겁화, 작열 보석 구매합니다";

    private static ExistingListingRecord Row(string price = "35,100") => new()
    {
        ListingNumber = Number,
        SourceListUrl = MyListingsImport.BuyListingsUrl,
        TradeSide = "구매", Game = "로스트아크", Server = "카마인",
        Title = Title, Price = price, Quantity = "1", Unit = "개"
    };

    private static ListingJob Job(string price = "30,000") => new()
    {
        ListingNumber = Number, SiteId = "slshrn124", ImportedFromSite = true,
        OriginalTradeSide = "구매", Game = "로스트아크", Server = "카마인",
        Title = Title, Price = price, Quantity = "1", Unit = "개",
        SourceListUrl = MyListingsImport.BuyListingsUrl,
        Enabled = true, AutoRepost = true, RequiresReview = false,
        NextDueUtc = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc),
        LastStatus = "예약됨"
    };

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Import price sync: " + message);
    }

    private static void Main()
    {
        Check(MyListingsImport.CleanImportedTitle("[1개~9,995개] 10레벨 겁화 보석 구매합니다") ==
            "10레벨 겁화 보석 구매합니다", "display quantity range is removed from title");
        Check(MyListingsImport.CleanImportedTitle("[ 3개 ~ 9,999개 ] 10레벨 작열 구매") ==
            "10레벨 작열 구매", "spaced split-trade prefix removed");
        Check(MyListingsImport.CleanImportedTitle("1 ~ 9,995 10레벨 겁화작열 삽니다") ==
            "10레벨 겁화작열 삽니다", "unbracketed split-trade prefix removed");
        Check(MyListingsImport.CleanImportedTitle("10레벨 겁화 보석 구매합니다") ==
            "10레벨 겁화 보석 구매합니다", "normal title stays unchanged");
        Check(MyListingsImport.HasSuspiciousDuplicatedLevel("1 ~ 999 9 9 레벨 겁화 구매합니다"),
            "duplicated level following split-range metadata must require review");
        Check(MyListingsImport.HasSuspiciousDuplicatedLevel("10 10 10 레벨 작열 구매합니다"),
            "triple duplicate level must require review");
        Check(!MyListingsImport.HasSuspiciousDuplicatedLevel("10레벨 겁화 보석 구매합니다"),
            "a normal level should not be quarantined");
        var rangedRow = Row();
        rangedRow.Title = "[1개~9,995개] " + Title;
        var firstImport = MyListingsImport.Prepare(new[] { rangedRow }, Array.Empty<ListingJob>(), "A", "slshrn124");
        Check(firstImport.NewJobs.Count == 1 && firstImport.NewJobs[0].Title == Title,
            "new imports save the actual product title, not displayed quantity range");
        Check(firstImport.NewJobs[0].SiteObservedTitle == rangedRow.Title,
            "raw site-rendered title is retained separately from saved title");
        var repeatedRow = Row(); repeatedRow.Title = "[1개~999개] 9 9 레벨 겁화 구매합니다";
        var repeatedImport = MyListingsImport.Prepare(new[] { repeatedRow },
            Array.Empty<ListingJob>(), "A", "slshrn124");
        Check(repeatedImport.NewJobs.Count == 1 && repeatedImport.NewJobs[0].RequiresReview &&
            !repeatedImport.NewJobs[0].Enabled, "repeated level title is held on its first import");

        var current = Job();
        var plan = MyListingsImport.Prepare(new[] { Row() }, new[] { current }, "A", "slshrn124");
        Check(plan.NewJobs.Count == 0 && plan.Updates.Count == 1, "one existing job updated");
        Check(plan.Updates[0].UpdateVerifiedPrice && plan.Updates[0].Price == "35,100", "verified site price selected");
        Check(plan.PriceObservations.Count == 1 && plan.PriceObservations[0].SavedPrice == "30,000" &&
            plan.PriceObservations[0].SitePrice == "35,100", "site and saved prices independently auditable");
        var scheduled = current.NextDueUtc;
        plan.Updates[0].Apply();
        Check(current.Price == "35,100", "30,000 becomes 35,100");
        Check(current.VerifiedSitePrice == "35,100" && current.VerifiedSitePriceUtc.HasValue,
            "confirmed own-list price is retained independently from editable target");
        Check(current.Enabled && current.AutoRepost && current.NextDueUtc == scheduled, "scheduler untouched");
        Check(!current.RequiresReview && current.LastStatus == "예약됨", "review and status untouched");

        var queued = Job("93,000");
        queued.PendingSitePriceTarget = "93000";
        var queuedPlan = MyListingsImport.Prepare(new[] { Row("90,000") },
            new[] { queued }, "A", "slshrn124");
        Check(queuedPlan.PriceObservations.Count == 1 &&
            queuedPlan.PriceObservations[0].Note.Contains("예약"), "reserved site price is visibly identified");
        Check(queuedPlan.Updates.All(x => !x.UpdateVerifiedPrice),
            "site re-import must not overwrite a user-approved pending price");
        foreach (var update in queuedPlan.Updates) update.Apply();
        Check(queued.Price == "93,000" && queued.PendingSitePriceTarget == "93000",
            "next-slot price reservation survives a site re-import");
        Check(queued.VerifiedSitePrice == "90,000",
            "pending edit retains actual confirmed site price, separate from reserved target");

        // A common live scenario is six same-server listings edited together.
        // Importing the signed-in My Room list during their wait must never
        // replace any of the six individually reserved prices.
        var sixQueued = Enumerable.Range(0, 6).Select(i =>
        {
            var job = Job("93,000");
            job.ListingNumber = (2026070508123456L + i).ToString();
            job.Server = "아브렐슈드";
            job.Title = Title + " " + i;
            job.PendingSitePriceTarget = "93000";
            return job;
        }).ToArray();
        var sixRecords = sixQueued.Select(job =>
        {
            var record = Row("90,000");
            record.ListingNumber = job.ListingNumber;
            record.Server = job.Server;
            record.Title = job.Title;
            return record;
        }).ToArray();
        var sixPlan = MyListingsImport.Prepare(sixRecords, sixQueued, "A", "slshrn124");
        Check(sixPlan.PriceObservations.Count == 6 &&
            sixPlan.Updates.All(x => !x.UpdateVerifiedPrice),
            "all six pending buy prices remain reserved across list refresh");
        foreach (var update in sixPlan.Updates) update.Apply();
        Check(sixQueued.All(job => job.Price == "93,000" && job.PendingSitePriceTarget == "93000"),
            "all six distinct owned listing IDs retain their own reservations");

        var same = MyListingsImport.Prepare(new[] { Row("35,100") }, new[] { current }, "A", "slshrn124");
        Check(same.Updates.Count == 0, "equal prices not duplicated");
        var withoutCommas = MyListingsImport.Prepare(new[] { Row("35100") }, new[] { current }, "A", "slshrn124");
        Check(withoutCommas.Updates.Count == 0, "equivalent number formatting not changed");

        var mismatchedTitle = Row(); mismatchedTitle.Title = "다른 물품";
        var changedTitleJob = Job();
        var sameIdChangedTitle = MyListingsImport.Prepare(new[] { mismatchedTitle },
            new[] { changedTitleJob }, "A", "slshrn124");
        Check(sameIdChangedTitle.Updates.Count == 1 && sameIdChangedTitle.Updates[0].UpdateVerifiedPrice,
            "same exact registration ID permits price refresh despite title-format changes");
        sameIdChangedTitle.Updates[0].Apply();
        Check(changedTitleJob.Price == "35,100" && changedTitleJob.Title == Title,
            "update price only, retain the user's existing title");
        Check(changedTitleJob.SiteObservedTitle == "다른 물품",
            "exact-ID re-import records current site title even when it differs from original");
        var samePriceDifferentTitle = Job("35,100");
        samePriceDifferentTitle.SiteObservedTitle = Title;
        var titleOnlyPlan = MyListingsImport.Prepare(new[] { mismatchedTitle },
            new[] { samePriceDifferentTitle }, "A", "slshrn124");
        Check(titleOnlyPlan.Updates.Count == 1 && !titleOnlyPlan.Updates[0].UpdateVerifiedPrice,
            "even an unchanged price must not hide a changed site title");
        titleOnlyPlan.Updates[0].Apply();
        Check(samePriceDifferentTitle.Title == Title && samePriceDifferentTitle.SiteObservedTitle == "다른 물품",
            "a title-only refresh retains the authored title unchanged");
        var wrongSide = Row(); wrongSide.TradeSide = "판매";
        Check(MyListingsImport.Prepare(new[] { wrongSide }, new[] { Job() }, "A", "slshrn124")
            .Updates.Count == 0, "trade side mismatch blocks price overwrite");
        var wrongNumber = Row(); wrongNumber.ListingNumber = "2026070508999999";
        Check(MyListingsImport.Prepare(new[] { wrongNumber }, new[] { Job() }, "A", "slshrn124")
            .Updates.Count == 0, "other number cannot overwrite this job");
        var manuallyCreated = Job(); manuallyCreated.ImportedFromSite = false;
        Check(MyListingsImport.Prepare(new[] { Row() }, new[] { manuallyCreated }, "A", "slshrn124")
            .Updates.Count == 0, "manual jobs never overwritten");
        var invalidPrice = Row("35,,100");
        var invalid = MyListingsImport.Prepare(new[] { invalidPrice }, new[] { Job() }, "A", "slshrn124");
        Check(invalid.Updates.Count == 0 && invalid.PriceObservations.Count == 1 &&
            invalid.PriceObservations[0].Note.Contains("미확인"), "invalid price is diagnosed but never copied");
        Check(MyListingsImport.Prepare(new[] { Row() }, new[] { Job() }, "A", "different-account")
            .Updates.Count == 0, "account mismatch never overwrites");
        Console.WriteLine("PASS exact owned-listing price refresh; all mismatch and schedule guards");
    }
}
