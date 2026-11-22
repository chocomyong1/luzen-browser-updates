using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal enum IntegratedEditField
{
    Price, Quantity, Title, Description, Character, IntervalMinutes, Enabled
}

internal readonly record struct IntegratedEditCommand(IntegratedEditField Field, string Value);

internal readonly record struct IntegratedEditResult(int Saved, int Deferred, BatchUndoToken UndoToken);

internal sealed partial class AutomationForm
{
    // MainForm can read the same live jobs without duplicating a scheduler,
    // browser profile or config. All changes go back to the owning system.
    internal IReadOnlyList<ListingJob> IntegratedEditJobs => _jobs.ToList();

    internal bool ValidateIntegratedEdit(IReadOnlyList<ListingJob> items,
        IntegratedEditCommand command, out string problem)
    {
        problem = "";
        if (items.Count == 0 || items.Distinct().Count() != items.Count ||
            items.Any(item => !_jobs.Contains(item)))
        {
            problem = "선택한 물품이 이 시스템의 현재 목록에 없습니다.";
            return false;
        }
        // Price edits are LOCAL reservations. When a site-price job is already
        // in flight, defer edits to that one listing until its readback ends.
        // Other rows are editable now; none start an independent site request.
        bool editingPendingPrices = command.Field == IntegratedEditField.Price &&
            _activeSitePriceJob is not null;
        if (_myListingsImportRunning || _sitePriceBatchRunning ||
            (_busy && _currentlyExecutingJob is null && !editingPendingPrices))
        {
            problem = "사이트 조회·가격 제출·기존 작업이 끝난 뒤 편집할 수 있습니다.";
            return false;
        }
        string value = command.Value.Trim();
        switch (command.Field)
        {
            case IntegratedEditField.Price:
                if (!long.TryParse(value, out long price) || price < 1000 || price % 10 != 0)
                    problem = "가격은 1,000원 이상, 10원 단위로 입력하세요.";
                break;
            case IntegratedEditField.Quantity:
                if (!int.TryParse(value, out int amount) || amount < 1)
                    problem = "수량은 1 이상으로 입력하세요.";
                break;
            case IntegratedEditField.Title:
            case IntegratedEditField.Description:
            case IntegratedEditField.Character:
                if (value.Length == 0) problem = "빈 값으로 일괄 변경할 수 없습니다.";
                break;
            case IntegratedEditField.IntervalMinutes:
                if (!int.TryParse(value, out int minutes) || minutes is < 1 or > 10080)
                    problem = "재등록 주기는 1~10080분 사이여야 합니다.";
                break;
            case IntegratedEditField.Enabled:
                if (value is not ("ON" or "OFF")) problem = "자동실행은 ON 또는 OFF를 선택하세요.";
                else if (value == "ON" && items.Any(j => j.RequiresReview))
                    problem = "확인 필요 물품은 자동실행을 켤 수 없습니다. 먼저 사이트 결과를 확인하세요.";
                break;
        }
        if (problem.Length > 0) return false;

        // This local editor does not publish a new title to the site. Avoid
        // misleading the user by changing only the saved display label.
        if (command.Field == IntegratedEditField.Title &&
            items.Any(item => ListingIdentity.HasNumber(item.ListingNumber)))
        {
            problem = "기존 사이트 등록글의 제목은 여기서 변경할 수 없습니다. 사이트 제목을 실제로 변경하는 기능이 아닙니다.";
            return false;
        }

        return command.Field != IntegratedEditField.Price ||
            TryPlanAutomaticPriceApply(items, value, out _, out problem);
    }

    private static void WriteIntegratedField(ListingJob item, IntegratedEditCommand command)
    {
        string value = command.Value.Trim();
        switch (command.Field)
        {
            case IntegratedEditField.Price:
                item.Price = value;
                break;
            case IntegratedEditField.Quantity: item.Quantity = value; break;
            case IntegratedEditField.Title: item.Title = value; break;
            case IntegratedEditField.Description: item.Description = command.Value; break;
            case IntegratedEditField.Character: item.Character = value; break;
            case IntegratedEditField.IntervalMinutes: item.IntervalMinutes = int.Parse(value); break;
            case IntegratedEditField.Enabled: item.Enabled = value == "ON"; break;
        }
    }

    private static IntegratedEditCommand SnapshotIntegratedField(ListingJob item, IntegratedEditField field) =>
        field switch
        {
            IntegratedEditField.Price => new(field, item.Price),
            IntegratedEditField.Quantity => new(field, item.Quantity),
            IntegratedEditField.Title => new(field, item.Title),
            IntegratedEditField.Description => new(field, item.Description),
            IntegratedEditField.Character => new(field, item.Character),
            IntegratedEditField.IntervalMinutes => new(field, item.IntervalMinutes.ToString()),
            _ => new(field, item.Enabled ? "ON" : "OFF")
        };

    internal IntegratedEditResult ApplyIntegratedEdit(IReadOnlyList<ListingJob> items,
        IntegratedEditCommand command)
    {
        if (!ValidateIntegratedEdit(items, command, out var problem))
            throw new InvalidOperationException(problem);

        var pendingByJob = new Dictionary<ListingJob, string>();
        if (command.Field == IntegratedEditField.Price &&
            !TryPlanAutomaticPriceApply(items, command.Value.Trim(), out pendingByJob, out problem))
            throw new InvalidOperationException(problem);

        var immediate = items.Where(job => !ReferenceEquals(job, _currentlyExecutingJob) &&
            !(command.Field == IntegratedEditField.Price && ReferenceEquals(job, _activeSitePriceJob))).ToList();
        var deferred = items.Except(immediate).ToList();
        var snapshots = immediate.Select(job => (Job: job,
            Previous: SnapshotIntegratedField(job, command.Field),
            Pending: job.PendingSitePriceTarget)).ToList();
        var undoChanges = items.Select(job => CaptureBatchUndoChange(job,
            current => SnapshotIntegratedField(current, command.Field).Value,
            (current, edited) => WriteIntegratedField(current,
                new IntegratedEditCommand(command.Field, edited)),
            command.Field == IntegratedEditField.Description ? command.Value : command.Value.Trim(),
            command.Field == IntegratedEditField.Price
                ? pendingByJob[job]
                : null)).ToList();
        var byJob = undoChanges.ToDictionary(change => change.Job);
        int queuedBefore = _deferredExecutingJobEdits.Count;
        try
        {
            foreach (var job in immediate) ApplyBatchUndoChange(byJob[job]);
            foreach (var job in deferred)
            {
                var change = byJob[job];
                Action afterSubmission = () => ApplyBatchUndoChange(change);
                change.DeferredAction = afterSubmission;
                _deferredExecutingJobEdits.Add(afterSubmission);
            }
            _config.Jobs = _jobs.ToList();
            _config.Save();
        }
        catch
        {
            foreach (var (job, previous, pending) in snapshots)
            {
                WriteIntegratedField(job, previous);
                job.PendingSitePriceTarget = pending;
            }
            _deferredExecutingJobEdits.RemoveRange(queuedBefore,
                _deferredExecutingJobEdits.Count - queuedBefore);
            _config.Jobs = _jobs.ToList();
            throw;
        }
        // Prevent a stale detail editor from writing its old values back.
        _selected = null;
        RefreshFilteredLists();
        AppendLog($"통합 일괄수정: {_system} {items.Count}건 · {command.Field} · " +
            (command.Field == IntegratedEditField.Price
                ? "기존 구매글은 로컬 가격 저장(사이트 별도 승인 필요), 신규글은 첫 등록에 적용"
                : "로컬 설정 저장") +
            (deferred.Count > 0 ? $" · 실행 중 {deferred.Count}건 사이트 작업 종료 후 적용" : ""));
        var receipt = RememberBatchUndo(command.Field.ToString(), undoChanges);
        return new IntegratedEditResult(immediate.Count, deferred.Count, receipt);
    }

    internal void OpenIntegratedJob(string id)
    {
        if (!_jobs.Any(job => job.Id == id)) return;
        ShowItemRegistrationView();
        RestoreEditorPanel();
        if (!_filteredJobs.Any(job => job.Id == id)) ResetFiltersToAll();
        RefreshFilteredLists(id);
        foreach (DataGridViewRow row in _jobsGrid.Rows)
        {
            if (row.DataBoundItem is not ListingJob job || job.Id != id) continue;
            _jobsGrid.ClearSelection();
            row.Selected = true;
            if (row.Cells.Count > 1) _jobsGrid.CurrentCell = row.Cells[1];
            SelectCurrentJob();
            break;
        }
    }
}
