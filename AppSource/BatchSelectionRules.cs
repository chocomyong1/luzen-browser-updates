using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LuzenBrowserReplica;

internal enum BatchScope { All, Sell, Buy }
internal enum BatchCriterion { All, SubId, SiteId, Game, Server, Category, Title, Price }

// Selection is local UI state only. It cannot submit, edit or enable a listing.
internal static class BatchSelectionRules
{
    public static bool IsInScope(ListingJob job, BatchScope scope)
    {
        if (scope == BatchScope.All) return true;
        var side = job.Operation switch
        {
            "판매등록" or "판매" => "판매",
            "구매등록" or "구매" => "구매",
            "재등록" => job.OriginalTradeSide?.Trim() ?? "",
            _ => ""
        };
        return scope == BatchScope.Sell ? side is "판매" or "판매등록" : side is "구매" or "구매등록";
    }

    public static bool TryPrice(string? input, out decimal price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var cleaned = input.Trim().Replace(",", "").Replace("₩", "").Replace("원", "")
                           .Replace(" ", "").Replace("\u00a0", "");
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture, out price) && price > 0;
    }

    private static bool Same(string? x, string? y) =>
        !string.IsNullOrWhiteSpace(x) && string.Equals(x.Trim(), y?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsValidAnchor(ListingJob anchor, BatchCriterion criterion) => criterion switch
    {
        BatchCriterion.SubId => !string.IsNullOrWhiteSpace(anchor.SubId),
        BatchCriterion.SiteId => !string.IsNullOrWhiteSpace(anchor.SiteId),
        BatchCriterion.Game => !string.IsNullOrWhiteSpace(anchor.Game),
        BatchCriterion.Server => !string.IsNullOrWhiteSpace(anchor.Game) && !string.IsNullOrWhiteSpace(anchor.Server),
        BatchCriterion.Category => !string.IsNullOrWhiteSpace(anchor.ItemCategory),
        BatchCriterion.Title => !string.IsNullOrWhiteSpace(anchor.Title),
        BatchCriterion.Price => TryPrice(anchor.Price, out _),
        _ => true
    };

    public static bool Matches(ListingJob candidate, ListingJob anchor, BatchCriterion criterion) => criterion switch
    {
        BatchCriterion.All => true,
        BatchCriterion.SubId => Same(candidate.SubId, anchor.SubId),
        BatchCriterion.SiteId => Same(candidate.SiteId, anchor.SiteId),
        BatchCriterion.Game => Same(candidate.Game, anchor.Game),
        // Server names may repeat across games. Never group across different games.
        BatchCriterion.Server => Same(candidate.Game, anchor.Game) && Same(candidate.Server, anchor.Server),
        BatchCriterion.Category => Same(candidate.ItemCategory, anchor.ItemCategory),
        BatchCriterion.Title => Same(candidate.Title, anchor.Title),
        BatchCriterion.Price => TryPrice(anchor.Price, out var target) && TryPrice(candidate.Price, out var other) && target == other,
        _ => false
    };

    public static List<ListingJob> Select(IReadOnlyList<ListingJob> visible,
        ListingJob anchor, BatchScope scope, BatchCriterion criterion)
    {
        if (!IsValidAnchor(anchor, criterion)) return new List<ListingJob>();
        return visible.Where(job => IsInScope(job, scope) && Matches(job, anchor, criterion)).ToList();
    }
}
