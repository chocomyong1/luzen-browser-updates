using System;
using System.Globalization;

namespace LuzenBrowserReplica;

// The official list exposes a short MM-dd HH:mm stamp, not a signed receipt.
// A large discrepancy can be detected, but never authorizes an extra submit.
internal static class SiteRepostAudit
{
    public static bool IsClearlyOlderThanSuccess(string siteStamp, DateTime lastSuccessUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(siteStamp) || lastSuccessUtc == DateTime.MinValue ||
            lastSuccessUtc > nowUtc.AddMinutes(2) || nowUtc - lastSuccessUtc > TimeSpan.FromDays(7)) return false;
        var successLocal = lastSuccessUtc.ToLocalTime();
        DateTime? best = null;
        foreach (int year in new[] { successLocal.Year - 1, successLocal.Year, successLocal.Year + 1 })
        {
            if (!DateTime.TryParseExact($"{year}-{siteStamp}", "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)) continue;
            if (best is null || Math.Abs((successLocal - parsed).TotalMinutes) <
                Math.Abs((successLocal - best.Value).TotalMinutes)) best = parsed;
        }
        return best.HasValue && successLocal - best.Value >= TimeSpan.FromMinutes(30) &&
            successLocal - best.Value <= TimeSpan.FromDays(7);
    }
}
