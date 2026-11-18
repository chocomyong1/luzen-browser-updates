using System;
using System.Collections.Generic;
using System.Linq;

namespace LuzenBrowserReplica;

// A one-step LOCAL edit receipt. Site submissions can never be undone here.
internal sealed class BatchUndoChange
{
    internal required ListingJob Job { get; init; }
    internal required Func<bool> MatchesBefore { get; init; }
    internal required Func<bool> MatchesAfter { get; init; }
    internal required Action RestoreBefore { get; init; }
    internal required Action RestoreAfter { get; init; }
    internal DateTime? SuccessAtApplication { get; set; }
    internal DateTime? PendingSubmitAtApplication { get; set; }
    internal bool ReviewAtApplication { get; set; }
    internal Action? DeferredAction { get; set; }
}

internal sealed class BatchUndoToken
{
    internal required string Label { get; init; }
    internal required List<BatchUndoChange> Changes { get; init; }
}

internal sealed partial class AutomationForm
{
    private BatchUndoToken? _lastBatchUndo;

    private static BatchUndoChange CaptureBatchUndoChange<T>(ListingJob job,
        Func<ListingJob, T> read, Action<ListingJob, T> write,
        T newValue, string? newPendingPrice)
    {
        T oldValue = read(job);
        string oldPendingPrice = job.PendingSitePriceTarget;
        bool Matches(T expected, string? pending) =>
            EqualityComparer<T>.Default.Equals(read(job), expected) &&
            (newPendingPrice is null ||
                string.Equals(job.PendingSitePriceTarget, pending, StringComparison.Ordinal));
        return new BatchUndoChange
        {
            Job = job,
            MatchesBefore = () => Matches(oldValue, oldPendingPrice),
            MatchesAfter = () => Matches(newValue, newPendingPrice),
            RestoreBefore = () =>
            {
                write(job, oldValue);
                if (newPendingPrice is not null) job.PendingSitePriceTarget = oldPendingPrice;
            },
            RestoreAfter = () =>
            {
                write(job, newValue);
                if (newPendingPrice is not null) job.PendingSitePriceTarget = newPendingPrice;
            },
            SuccessAtApplication = job.LastSucceededUtc,
            PendingSubmitAtApplication = job.PendingRepostAttemptUtc,
            ReviewAtApplication = job.RequiresReview
        };
    }

    private static void ApplyBatchUndoChange(BatchUndoChange change)
    {
        change.RestoreAfter();
        // For a running submission, the baseline is the moment its deferred
        // edit actually runs, not the earlier moment when the user clicked.
        change.SuccessAtApplication = change.Job.LastSucceededUtc;
        change.PendingSubmitAtApplication = change.Job.PendingRepostAttemptUtc;
        change.ReviewAtApplication = change.Job.RequiresReview;
    }

    private BatchUndoToken RememberBatchUndo(string label, List<BatchUndoChange> changes)
    {
        var receipt = new BatchUndoToken { Label = label, Changes = changes };
        _lastBatchUndo = receipt;
        return receipt;
    }

    internal bool CanUndoBatchEdit(BatchUndoToken? token, out string reason)
    {
        reason = "";
        if (token is null || !ReferenceEquals(token, _lastBatchUndo))
        {
            reason = "마지막 수정 기록이 아니거나 이후에 다른 일괄수정이 실행됐습니다.";
            return false;
        }
        if (_myListingsImportRunning || _sitePriceBatchRunning ||
            (_busy && _currentlyExecutingJob is null))
        {
            reason = "사이트 조회·제출이 끝난 뒤 로컬 변경을 되돌릴 수 있습니다.";
            return false;
        }
        foreach (var change in token.Changes)
        {
            if (!_jobs.Contains(change.Job))
            {
                reason = "수정 이후 목록이 바뀌었습니다. 현재 물품을 복구할 수 없습니다.";
                return false;
            }
            bool pending = change.DeferredAction is not null &&
                _deferredExecutingJobEdits.Contains(change.DeferredAction);
            if (ReferenceEquals(change.Job, _currentlyExecutingJob) && !pending)
            {
                reason = "이 물품을 실제 등록하는 중입니다. 작업 종료 후 다시 시도하세요.";
                return false;
            }
            if (pending ? !change.MatchesBefore() : !change.MatchesAfter())
            {
                reason = "수정 이후 물품 값이나 가격 예약이 바뀌었습니다. 다른 변경을 덮어쓰지 않도록 복구하지 않습니다.";
                return false;
            }
            if (!pending && (change.Job.LastSucceededUtc != change.SuccessAtApplication ||
                change.Job.PendingRepostAttemptUtc != change.PendingSubmitAtApplication ||
                change.Job.RequiresReview != change.ReviewAtApplication))
            {
                reason = "수정 이후 사이트 등록이 진행됐습니다. 사이트 상태와 어긋날 수 있어 자동으로 되돌리지 않습니다.";
                return false;
            }
        }
        return true;
    }

    internal bool TryUndoBatchEdit(BatchUndoToken? token, out string reason)
    {
        if (!CanUndoBatchEdit(token, out reason)) return false;
        var changes = token!.Changes;
        var pending = changes.Where(change => change.DeferredAction is not null &&
            _deferredExecutingJobEdits.Contains(change.DeferredAction)).ToList();
        var applied = changes.Except(pending).ToList();
        foreach (var change in pending)
            _deferredExecutingJobEdits.Remove(change.DeferredAction!);
        foreach (var change in applied) change.RestoreBefore();
        try
        {
            _config.Jobs = _jobs.ToList();
            _config.Save();
        }
        catch (Exception ex)
        {
            foreach (var change in applied) change.RestoreAfter();
            foreach (var change in pending) _deferredExecutingJobEdits.Add(change.DeferredAction!);
            _config.Jobs = _jobs.ToList();
            reason = "되돌린 설정 저장에 실패하여 변경을 유지했습니다: " + ex.Message;
            return false;
        }
        _lastBatchUndo = null;
        _selected = null; // Don't let stale detail inputs write back the edited field.
        RefreshFilteredLists();
        AppendLog($"마지막 일괄수정 되돌림 (로컬만): {_system} · {token.Label} · {changes.Count}건");
        reason = $"{token.Label} · {changes.Count}건의 로컬 변경을 복구했습니다. 사이트에 제출된 변경은 복구하지 않았습니다.";
        return true;
    }

    private void UndoRecentBatchEdit()
    {
        if (!CanUndoBatchEdit(_lastBatchUndo, out var reason))
        {
            System.Windows.Forms.MessageBox.Show(this, reason, "일괄수정 되돌리기");
            return;
        }
        if (System.Windows.Forms.MessageBox.Show(this,
            $"마지막 '{_lastBatchUndo!.Label}' 변경을 로컬 물품 설정에서 되돌릴까요?\n\n" +
            "사이트에 이미 제출된 작업은 되돌리지 않습니다.",
            "일괄수정 되돌리기", System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question,
            System.Windows.Forms.MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes)
            return;
        TryUndoBatchEdit(_lastBatchUndo, out reason);
        System.Windows.Forms.MessageBox.Show(this, reason, "일괄수정 되돌리기");
    }
}
