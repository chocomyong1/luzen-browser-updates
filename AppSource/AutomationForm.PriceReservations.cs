using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

internal readonly record struct PriceReservationSnapshot(
    ListingJob Job, string Status, DateTime? EstimatedUtc, int EarlierJobs);

internal sealed partial class AutomationForm
{
    // Cancel only an unsubmitted LOCAL price target. This never touches the
    // browser or the actual site price; keep any uncertain submit evidence.
    internal bool CanCancelUnsubmittedPriceReservation(ListingJob job, out string problem)
    {
        problem = "";
        if (!_jobs.Contains(job) || string.IsNullOrWhiteSpace(job.PendingSitePriceTarget))
        {
            problem = "이 시스템에 취소할 가격 요청이 없습니다.";
            return false;
        }
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning ||
            ReferenceEquals(job, _activeSitePriceJob) || ReferenceEquals(job, _currentlyExecutingJob))
        {
            problem = "사이트 작업 중에는 가격 요청을 취소할 수 없습니다. 작업이 끝난 뒤 다시 시도하세요.";
            return false;
        }
        bool legacyHold = job.RequiresReview &&
            job.LastStatus == "제목 보호 모드: 가격 예약 자동 제출 중지";
        if ((job.RequiresReview && !legacyHold) ||
            job.PendingRepostAttemptUtc.HasValue ||
            !string.IsNullOrWhiteSpace(job.PendingRepostBeforeStamp) ||
            !string.IsNullOrWhiteSpace(job.PriceEditSiteTitleSnapshot))
        {
            problem = "이 글은 과거 제출 결과가 불확실할 수 있어 예약만 취소할 수 없습니다. " +
                "먼저 '가격 변경 결과 재조회 (제출 없음)'로 사이트 상태를 확인해 주세요.";
            return false;
        }
        return true;
    }

    internal bool TryCancelUnsubmittedPriceReservation(ListingJob job, out string problem)
    {
        if (!CanCancelUnsubmittedPriceReservation(job, out problem)) return false;
        string beforeTarget = job.PendingSitePriceTarget;
        string beforeStatus = job.LastStatus;
        bool beforeReview = job.RequiresReview;
        bool beforeEnabled = job.Enabled;
        DateTime beforeDue = job.NextDueUtc;
        try
        {
            job.PendingSitePriceTarget = "";
            if (job.RequiresReview)
            {
                job.RequiresReview = false;
                job.Enabled = false;
                job.NextDueUtc = DateTime.MaxValue;
            }
            job.LastStatus = "미제출 가격 요청 취소 · 사이트 가격 미변경";
            _config.Jobs = _jobs.ToList();
            _config.Save();
            AppendLog("가격 예약 취소(사이트 미제출): " + job.Server + " · " + job.ListingNumber);
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
            return true;
        }
        catch (Exception ex)
        {
            job.PendingSitePriceTarget = beforeTarget;
            job.LastStatus = beforeStatus;
            job.RequiresReview = beforeReview;
            job.Enabled = beforeEnabled;
            job.NextDueUtc = beforeDue;
            problem = "예약 취소 내용을 저장하지 못해 변경을 되돌렸습니다: " + ex.Message;
            return false;
        }
    }

    internal bool CanReconcilePriceReservation(ListingJob job) =>
        !_busy && !_myListingsImportRunning && !_sitePriceBatchRunning && !_siteRequestTraceActive &&
        _jobs.Contains(job) && IsRegisteredOwnedBuyPriceTarget(job) && job.RequiresReview &&
        job.LastStatus.Contains("가격", StringComparison.Ordinal) &&
        !job.PendingRepostAttemptUtc.HasValue && string.IsNullOrWhiteSpace(job.PendingRepostBeforeStamp) &&
        TrySitePrice(job.PendingSitePriceTarget, out _);

    internal async Task<string?> ReconcilePriceReservationAsync(ListingJob job)
    {
        if (!CanReconcilePriceReservation(job))
            return "이 요청은 지금 조회할 수 없습니다.";
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))
            return "독립 네트워크 연결을 확인할 수 없습니다.";
        string account = ItemManiaAccountStore.Load(_system).UserId;
        if (string.IsNullOrWhiteSpace(account) ||
            !string.Equals(job.SiteId, account, StringComparison.OrdinalIgnoreCase))
            return "현재 로그인 계정과 등록글 계정이 다릅니다.";
        if (!TrySitePrice(job.PendingSitePriceTarget, out long target))
            return "이전 요청 가격을 확인할 수 없습니다.";

        _busy = true;
        _activeSitePriceJob = job;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_web.CoreWebView2 is null) return "사이트 브라우저를 열 수 없습니다.";
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(cancellation.Token);
            var result = await ReadSitePriceAfterRepostAsync(job, target, cancellation.Token);
            if (result.ExactCurrentPrice is not long current ||
                (!result.ExactIdVerified &&
                 (!TrySitePrice(job.VerifiedSitePrice, out var oldPrice) || current != oldPrice)))
                return result.Message;

            var previous = (job.Price, job.VerifiedSitePrice, job.VerifiedSitePriceUtc,
                job.PendingSitePriceTarget, job.PriceEditSiteTitleSnapshot,
                job.RequiresReview, job.Enabled, job.NextDueUtc, job.LastStatus);
            try
            {
                job.Price = current.ToString("N0");
                job.VerifiedSitePrice = current.ToString();
                job.VerifiedSitePriceUtc = DateTime.UtcNow;
                job.PendingSitePriceTarget = "";
                job.PriceEditSiteTitleSnapshot = "";
                job.RequiresReview = false;
                job.Enabled = false;
                job.NextDueUtc = DateTime.MaxValue;
                job.LastStatus = result.ExactIdVerified ? "사이트 가격 확인 완료 · 자동 OFF" :
                    "기존 사이트 가격 확인 · 미반영 요청 정리 · 자동 OFF";
                _config.Jobs = _jobs.ToList();
                _config.Save();
            }
            catch
            {
                (job.Price, job.VerifiedSitePrice, job.VerifiedSitePriceUtc,
                    job.PendingSitePriceTarget, job.PriceEditSiteTitleSnapshot,
                    job.RequiresReview, job.Enabled, job.NextDueUtc, job.LastStatus) = previous;
                throw;
            }
            AppendLog($"가격 요청 정리 (사이트 제출 없음): {job.ListingNumber} · {current:N0}원");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            _activeSitePriceJob = null;
            _busy = false;
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
            DispatchQueuedMyListingsImport();
        }
    }

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
            if (job.RequiresReview)
            {
                snapshots.Add(new(job, "확인 필요 · 자동 보류", null, 0)); continue;
            }
            if (!SitePriceEdit.AutomaticPriceSubmissionEnabled)
            {
                snapshots.Add(new(job, "저장됨 · 사이트 제출 대기", null, 0)); continue;
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
