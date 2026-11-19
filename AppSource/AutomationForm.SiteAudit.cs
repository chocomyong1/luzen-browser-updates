using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private DateTime _nextSiteAuditUtc = DateTime.UtcNow.AddMinutes(15);
    private CancellationTokenSource? _siteAuditRun;

    private async Task AuditOneConfirmedListingAsync()
    {
        _nextSiteAuditUtc = DateTime.UtcNow.AddMinutes(15);
        if (!_schedulerRunning || _busy || _myListingsImportRunning || _pendingMyListingsImport ||
            (_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system)) return;
        DateTime now = DateTime.UtcNow;
        var candidate = _jobs.Where(job => job.Enabled && !job.RequiresReview &&
                job.InitialCompleted && job.LastSucceededUtc.HasValue &&
                now - job.LastSucceededUtc.Value >= TimeSpan.FromMinutes(10) &&
                job.NextDueUtc > now.AddMinutes(10) &&
                !job.PendingRepostAttemptUtc.HasValue &&
                ListingIdentity.HasNumber(job.ListingNumber) &&
                MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job)) &&
                (!job.LastSiteAuditUtc.HasValue ||
                 now - job.LastSiteAuditUtc.Value >= TimeSpan.FromMinutes(job.SiteAuditMismatchCount > 0 ? 2 : 15)))
            .OrderByDescending(job => job.SiteAuditMismatchCount)
            .ThenBy(job => job.LastSiteAuditUtc ?? DateTime.MinValue)
            .FirstOrDefault();
        if (candidate is null) return;
        _busy = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        _siteAuditRun = cancellation;
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(cancellation.Token);
            await NavigateOwnedListAsync(OwnedListUrlFor(candidate), cancellation.Token);
            var row = await LocateOwnedListRepostAsync(candidate, cancellation.Token, requireRepostButton: false);
            if (row.Status != "found" || string.IsNullOrWhiteSpace(row.Stamp)) return;
            bool mismatch = SiteRepostAudit.IsClearlyOlderThanSuccess(row.Stamp,
                candidate.LastSucceededUtc!.Value, DateTime.UtcNow);
            candidate.LastSiteAuditUtc = DateTime.UtcNow;
            candidate.SiteAuditMismatchCount = mismatch ? candidate.SiteAuditMismatchCount + 1 : 0;
            if (candidate.SiteAuditMismatchCount >= 2)
            {
                // Repeatable, affirmative site evidence disagrees with the
                // local success time. Only THIS job is held, never reposted.
                candidate.RequiresReview = true;
                candidate.NextDueUtc = DateTime.MaxValue;
                candidate.LastStatus = "사이트 재등록 시각 재확인 필요";
                AppendLog($"사이트 시각 2회 불일치 · {candidate.Server} · {candidate.ListingNumber} · 해당 글만 보류 (재제출 없음)");
                NotifyAutomationIssue("재등록 시각 불일치", "사이트에 표시된 재등록 시각이 로컬 성공 기록과 2회 연속 다릅니다.", candidate);
            }
            else if (mismatch)
            {
                _nextSiteAuditUtc = DateTime.UtcNow.AddMinutes(2);
                AppendLog($"사이트 시각 재검사 예정 · {candidate.Server} · {candidate.ListingNumber} · 아직 작업 보류 없음");
            }
            _config.Save();
            _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshAutoSummary();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppendLog($"사이트 시각 읽기 전용 검사 보류: {ex.GetType().Name} · 등록 작업 영향 없음");
        }
        finally
        {
            if (ReferenceEquals(_siteAuditRun, cancellation)) _siteAuditRun = null;
            _busy = false;
            DispatchQueuedMyListingsImport();
        }
    }
}
