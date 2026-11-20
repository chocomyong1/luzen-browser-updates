using System;
using System.Collections.Generic;
using System.Linq;

namespace LuzenBrowserReplica;

internal readonly record struct PriceReservationSnapshot(
    ListingJob Job, string Status, DateTime? EstimatedUtc, int EarlierJobs);

internal sealed partial class AutomationForm
{
    // Read-only view of the live scheduler. EstimatedUtc is NOT a promised
    // execution timestamp: site login, shared quotas and processing time can
    // delay it, and stopped/held systems intentionally show no estimate.
    internal IReadOnlyList<PriceReservationSnapshot> CurrentPriceReservations()
    {
        DateTime now = DateTime.UtcNow;
        var scheduled = _jobs.Where(WantsWork)
            .OrderBy(j => j.LastSucceededUtc ?? DateTime.MinValue)
            .ThenBy(j => j.NextDueUtc).ToList();
        var snapshots = new List<PriceReservationSnapshot>();
        foreach (var job in _jobs.Where(j => !string.IsNullOrWhiteSpace(j.PendingSitePriceTarget)))
        {
            if (!SitePriceEdit.AutomaticPriceSubmissionEnabled)
            {
                snapshots.Add(new(job, "제목 보호 모드 · 사이트 가격 자동 제출 중지", null, 0)); continue;
            }
            if (job.RequiresReview)
            {
                snapshots.Add(new(job, "확인 필요 · 자동 보류", null, 0)); continue;
            }
            if (!job.Enabled)
            {
                snapshots.Add(new(job, "자동 실행 OFF", null, 0)); continue;
            }
            if (!_schedulerRunning)
            {
                snapshots.Add(new(job, "자동 시작 후 계산", null, 0)); continue;
            }
            if (_loginRecoveryPauseUntilUtc != DateTime.MinValue ||
                _ownedListRecoveryUtc != DateTime.MinValue ||
                _bConnectionRecoveryUtc != DateTime.MinValue)
            {
                snapshots.Add(new(job, "사이트 연결 복구 대기", null, 0)); continue;
            }
            if (job.NextDueUtc == DateTime.MaxValue)
            {
                snapshots.Add(new(job, "다음 순번 미정", null, 0)); continue;
            }
            var counter = HourlyServerQuota.Current(_config.ServerHourlyCounters, job, now);
            if (HourlyServerQuota.Remaining(counter, _config.MaxSubmissionsPerHour) <= 0)
            {
                snapshots.Add(new(job, "시간당 등록 한도 대기", null, 0)); continue;
            }
            string key = HourlyServerQuota.Key(job);
            DateTime lastServer = _config.LastServerAttemptUtc.TryGetValue(key, out var recent) ? recent :
                _config.LastServerAttemptUtc.TryGetValue(job.Server, out var legacy) ? legacy : DateTime.MinValue;
            DateTime siteEarliest = HourlyServerQuota.NextAllowedUtc(lastServer, now, counter,
                _config.MaxSubmissionsPerHour, _config.MinimumServerSeconds);
            DateTime due = job.NextDueUtc > now ? job.NextDueUtc : now;
            if (siteEarliest > due) due = siteEarliest;
            int position = scheduled.IndexOf(job);
            int ahead = position < 0 ? 0 : scheduled.Take(position).Count(j => j.NextDueUtc <= due);
            // Approximate total queue work, not a second scheduler or a
            // guaranteed arrival. A/B/C are independently queued.
            double secondsPerJob = Math.Max(8, Math.Min(30, _config.MinimumServerSeconds / 4.0));
            DateTime estimated = due.AddSeconds(ahead * secondsPerJob);
            snapshots.Add(new(job, "다음 등록 순번 예약", estimated, ahead));
        }
        return snapshots;
    }
}
