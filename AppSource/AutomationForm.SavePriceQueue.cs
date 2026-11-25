using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

// Save is the user's one approval for the precise set of saved price targets.
// New listings are only saved locally. Existing OWN buy listings are processed
// one at a time, never by manipulating their listing numbers or posting twice.
internal readonly record struct SavedPriceApplyResult(int Planned, int Verified, string Failure)
{
    public bool Succeeded => string.IsNullOrEmpty(Failure);
}

internal sealed partial class AutomationForm
{
    internal async Task<SavedPriceApplyResult> ApplySavedPricesSequentialAsync(
        IReadOnlyList<ListingJob> requested)
    {
        var targets = requested.Distinct()
            .Where(job => _jobs.Contains(job) &&
                !IsUnsubmittedNewListing(job) &&
                !string.IsNullOrWhiteSpace(job.PendingSitePriceTarget))
            .ToList();
        if (targets.Count == 0) return new SavedPriceApplyResult(0, 0, "");

        // The saved queue is a local snapshot. It does not authorize another
        // site request when a previous attempt is ambiguous or an import/edit
        // is already in flight.
        string problem = "";
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning ||
            _siteRequestTraceActive || _activeSitePriceJob is not null)
            problem = "다른 사이트 작업이 진행 중입니다. 가격은 저장됐지만 사이트에는 아직 반영되지 않았습니다.";
        string account = ItemManiaAccountStore.Load(_system).UserId;
        if (problem.Length == 0 && (string.IsNullOrWhiteSpace(account) ||
            ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))))
            problem = "사이트 계정 또는 독립 연결을 확인할 수 없습니다. 저장된 가격은 사이트에 제출하지 않았습니다.";
        if (problem.Length == 0 && (targets.Any(job =>
                !IsRegisteredOwnedBuyPriceTarget(job) || job.RequiresReview ||
                !string.Equals(job.SiteId, account, StringComparison.OrdinalIgnoreCase) ||
                !TrySitePrice(job.PendingSitePriceTarget, out long target) ||
                !TrySitePrice(job.Price, out long saved) ||
                target != saved || target < 1000 || target % 10 != 0 ||
                job.LastStatus.Contains("결과 확인 필요", StringComparison.Ordinal)) ||
            targets.GroupBy(job => (job.SiteId.ToLowerInvariant(), job.ListingNumber))
                .Any(group => group.Count() > 1) ||
            targets.Any(job => _jobs.Any(other => !ReferenceEquals(job, other) &&
                string.Equals(job.SiteId, other.SiteId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(job.ListingNumber, other.ListingNumber, StringComparison.Ordinal)))))
            problem = "등록번호·계정·저장 가격 또는 확인 대기 상태가 일치하지 않습니다. 중복 제출을 막기 위해 시작하지 않았습니다.";

        if (problem.Length > 0)
        {
            AppendLog("저장 후 순차 가격 반영 보류: " + problem);
            SitePriceBatchProgress?.Invoke("사이트 미반영 · " + problem, false, problem);
            return new SavedPriceApplyResult(targets.Count, 0, problem);
        }

        if (_schedulerRunning) PauseScheduler();
        // Only one sequential runner per A/B/C system; MainForm awaits these
        // runners in A -> B -> C order instead of launching three at once.
        _sitePriceBatchCancellation = new CancellationTokenSource();
        _sitePriceBatchRunning = true;
        int verified = 0;
        string failure = "";
        SitePriceBatchProgress?.Invoke($"0/{targets.Count}건 확인 · 저장 후 순차 가격 반영", true, null);
        AppendLog($"저장 후 순차 가격 반영 시작: {_system} {targets.Count}건");
        try
        {
            foreach (var job in targets)
            {
                CancellationToken token = _sitePriceBatchCancellation.Token;
                token.ThrowIfCancellationRequested();
                _reportSitePriceBatchStage = stage => SitePriceBatchProgress?.Invoke(
                    $"{verified}/{targets.Count}건 확인 · {job.Server} · {job.ListingNumber} · " + stage,
                    true, null);

                // An in-flight price may have been edited again after saving.
                // Never submit an obsolete amount.
                if (!TrySitePrice(job.PendingSitePriceTarget, out var target) ||
                    !TrySitePrice(job.Price, out var saved) || target != saved ||
                    job.RequiresReview)
                {
                    failure = job.ListingNumber + " · 가격이 변경되었거나 확인 대기 중이므로 제출하지 않았습니다.";
                    break;
                }

                while (!CanRunNow(job, out var spacing))
                {
                    if (spacing.Contains("이번 시간 등록 가능 횟수", StringComparison.Ordinal))
                    {
                        failure = spacing;
                        break;
                    }
                    _state.Text = "가격 순차 반영 간격 대기 · " + spacing;
                    _reportSitePriceBatchStage?.Invoke(spacing);
                    await Task.Delay(1000, token);
                }
                if (failure.Length > 0) break;

                _state.Text = $"가격 순차 반영 {verified + 1}/{targets.Count} · 등록번호 {job.ListingNumber}";
                if (!await ApplySitePriceAsync(job, bulkApproval: true, nativeSingleApproval: true,
                    manuallyApprovedPrice: target, backgroundApproved: true))
                {
                    failure = job.ListingNumber + " · " + _lastSitePriceEditFailure;
                    break; // A failed/uncertain submission is never retried.
                }
                verified++;
            }
        }
        catch (OperationCanceledException)
        {
            failure = "중지 요청: 다음 글은 제출하지 않았습니다. 현재 글의 사이트 결과는 확인해야 할 수 있습니다.";
        }
        catch (Exception ex)
        {
            failure = "순차 적용 중단: " + ex.Message;
        }
        finally
        {
            _sitePriceBatchRunning = false;
            _reportSitePriceBatchStage = null;
            _sitePriceBatchCancellation.Dispose();
            _sitePriceBatchCancellation = null;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
        }

        string progress = failure.Length == 0
            ? $"완료 · {verified}/{targets.Count}건 사이트 가격 확인"
            : $"중단 · {verified}/{targets.Count}건 확인 · {failure}";
        AppendLog("저장 후 순차 가격 반영: " + progress);
        SitePriceBatchProgress?.Invoke(progress, false, failure.Length > 0 ? failure : null);
        return new SavedPriceApplyResult(targets.Count, verified, failure);
    }

    // The existing detail editor Save button is the ONLY submission approval
    // for that selected row. Closing the editor or changing selection is not.
    private async Task SaveSingleEditorAndApplyPriceAsync()
    {
        var job = _selected;
        if (job is null) return;
        SaveSelectedJob();
        RefreshFilterChoices();
        RefreshFilteredLists();
        if (!ReferenceEquals(job, _selected) && !_jobs.Contains(job)) return;
        var result = await ApplySavedPricesSequentialAsync(new[] { job });
        if (!result.Succeeded)
            AppendLog("단일편집 저장: 가격은 저장됨 · 사이트 순차 적용은 중단됨");
    }
}
