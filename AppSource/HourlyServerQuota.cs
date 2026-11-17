using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

// One quota per signed-in site ID, game, and server. The site explicitly reports
// the *remaining* ID count after a successful repost. IP counts are not used.
internal sealed class HourlyServerCounter
{
    public string HourKey { get; set; } = "";
    public int Attempts { get; set; }
    public int? SiteRemaining { get; set; }
    public string LastNotifiedDifference { get; set; } = "";
}

internal readonly record struct HourlyQuotaDifference(int Expected, int Reported, int Effective);

internal static class HourlyServerQuota
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);

    public static string Key(ListingJob job) =>
        (job.SiteId ?? "").Trim().ToLowerInvariant() + "|" +
        (job.Game ?? "").Trim().ToLowerInvariant() + "|" +
        (job.Server ?? "").Trim().ToLowerInvariant();

    public static string HourKey(DateTime utc) =>
        utc.ToUniversalTime().Add(KoreaOffset).ToString("yyyy-MM-dd HH");

    public static DateTime NextHourUtc(DateTime utc)
    {
        var korea = utc.ToUniversalTime().Add(KoreaOffset);
        var next = new DateTime(korea.Year, korea.Month, korea.Day, korea.Hour, 0, 0)
            .AddHours(1).Subtract(KoreaOffset);
        return DateTime.SpecifyKind(next, DateTimeKind.Utc);
    }

    public static HourlyServerCounter Current(Dictionary<string, HourlyServerCounter> counters,
        ListingJob job, DateTime utc)
    {
        string key = Key(job);
        string hour = HourKey(utc);
        if (!counters.TryGetValue(key, out var state) || state is null)
            counters[key] = state = new HourlyServerCounter();
        if (state.HourKey != hour)
        {
            state.HourKey = hour;
            state.Attempts = 0;
            state.SiteRemaining = null;
            state.LastNotifiedDifference = "";
        }
        return state;
    }

    public static int Remaining(HourlyServerCounter state, int configuredPerHour)
    {
        int configuredRemaining = Math.Max(0, configuredPerHour - Math.Max(0, state.Attempts));
        return state.SiteRemaining.HasValue
            ? Math.Min(configuredRemaining, Math.Max(0, state.SiteRemaining.Value))
            : configuredRemaining;
    }

    // Checkpoint immediately before the real submit action. An uncertain submit
    // still consumes one *attempt* to avoid repeating it after a restart.
    public static void MarkAttempt(HourlyServerCounter state)
    {
        state.Attempts++;
        if (state.SiteRemaining.HasValue)
            state.SiteRemaining = Math.Max(0, state.SiteRemaining.Value - 1);
    }

    // Called only after the exact listing and the site's success response were
    // verified. The first official site count in each ID/server/Korean hour is
    // an authoritative baseline, not a discrepancy: the user may already have
    // used registrations outside the program earlier in that same hour.
    // Reconcile without pausing other servers. Caller sends Telegram only for
    // subsequent, genuinely unexpected changes to the site's remaining count.
    public static HourlyQuotaDifference? Reconcile(HourlyServerCounter state,
        int configuredPerHour, int reportedRemaining)
    {
        bool hasOfficialBaseline = state.SiteRemaining.HasValue;
        // Compare official counts against the previous official count, rather
        // than the stricter user-configured cap. A configured cap of 30 with
        // 59 site registrations left must NOT cause an alert after every post.
        int expected = state.SiteRemaining ?? Remaining(state, configuredPerHour);
        state.SiteRemaining = Math.Max(0, reportedRemaining);
        int effective = Remaining(state, configuredPerHour);
        if (!hasOfficialBaseline || expected == reportedRemaining) return null;
        // Suppress per-post repeats of the same discrepancy. When the site has
        // a larger quota than the user's setting, both remaining counts fall
        // after each repost but the observed hourly capacity stays constant.
        string signature = configuredPerHour + "/" + (reportedRemaining + state.Attempts);
        if (state.LastNotifiedDifference == signature) return null;
        state.LastNotifiedDifference = signature;
        return new HourlyQuotaDifference(expected, reportedRemaining, effective);
    }

    public static DateTime NextAllowedUtc(DateTime lastAttemptUtc, DateTime nowUtc,
        HourlyServerCounter state, int configuredPerHour, int minServerSeconds)
    {
        if (lastAttemptUtc == DateTime.MinValue) return nowUtc;
        int spacing = Math.Max(21, minServerSeconds);
        if (HourKey(lastAttemptUtc) == HourKey(nowUtc))
        {
            int remaining = Remaining(state, configuredPerHour);
            if (remaining > 0)
                spacing = Math.Max(spacing, (int)Math.Ceiling(
                    Math.Max(0, (NextHourUtc(lastAttemptUtc) - lastAttemptUtc).TotalSeconds) / remaining));
        }
        return lastAttemptUtc.AddSeconds(spacing);
    }

    public static int? ParseIdRemaining(string? siteMessage)
    {
        if (string.IsNullOrWhiteSpace(siteMessage) ||
            !siteMessage.Contains("추가등록", StringComparison.Ordinal)) return null;
        // Example: "19시까지 해당 ID로 17개 / IP에서 27개까지 추가등록 가능합니다."
        // Never pick up the unrelated IP count or guess a quota from a title.
        var match = Regex.Match(siteMessage,
            @"(?:해당\s*)?(?:ID|아이디)\s*(?:로|에서|당)?\s*(?<count>\d{1,4})\s*개",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["count"].Value, out int remaining)
            ? remaining : null;
    }
}
