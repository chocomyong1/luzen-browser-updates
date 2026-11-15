using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // V18: A tri-state check box lives IN the table's first column header.
    // It selects the currently displayed/search-filtered rows, not hidden jobs.
    // This affects only SelectedForBulk: it must never silently enable live posting.
    private bool _applyingHeaderBulkSelection;

    private void InstallSelectAllHeaders()
    {
        InstallSelectAllHeader(_jobsGrid);
        InstallSelectAllHeader(_autoGrid);
        InstallAutomaticHeader(_jobsGrid);
        InstallAutomaticHeader(_autoGrid);
        RefreshSelectAllHeaderVisuals();
    }

    // Second independent tri-state header: the user can switch automation
    // for the current filtered rows without clicking each row individually.
    // RequiresReview rows remain OFF; this header never bypasses safety checks.
    private void InstallAutomaticHeader(DataGridView grid)
    {
        var col = grid.Columns["Enabled"];
        if (col is null) return;
        col.HeaderText = "자동";
        col.MinimumWidth = 64;
        col.Width = Math.Max(col.Width, 64);
        col.SortMode = DataGridViewColumnSortMode.NotSortable;
        col.HeaderCell.ToolTipText = "클릭: 조회 결과 전체 자동 ON/OFF (확인 필요 물품 제외)";

        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex != -1 || e.ColumnIndex != col.Index) return;
            e.PaintBackground(e.CellBounds, false);
            e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
            var visible = CurrentGridJobs(grid);
            int enabled = visible.Count(job => job.Enabled);
            var state = visible.Count > 0 && enabled == visible.Count
                ? CheckBoxState.CheckedNormal
                : enabled > 0 ? CheckBoxState.MixedNormal : CheckBoxState.UncheckedNormal;
            int y = e.CellBounds.Top + (e.CellBounds.Height - 14) / 2;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.CellBounds.Left + 5, y), state);
            var rect = new Rectangle(e.CellBounds.Left + 24, e.CellBounds.Top + 1,
                Math.Max(12, e.CellBounds.Width - 26), e.CellBounds.Height - 2);
            TextRenderer.DrawText(e.Graphics, "자동", e.CellStyle.Font ?? Font, rect,
                UiTheme.Text, TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        };
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex == col.Index)
                ToggleGridHeaderAutomatic(grid);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == col.Index && !_applyingHeaderBulkSelection)
                RefreshSelectAllHeaderVisuals();
        };
    }

    private void ToggleGridHeaderAutomatic(DataGridView grid)
    {
        if (_applyingHeaderBulkSelection) return;
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var shown = CurrentGridJobs(grid);
        if (shown.Count == 0) return;
        if (_busy)
        {
            MessageBox.Show(this, _myListingsImportRunning
                ? "내 등록글 조회가 진행 중입니다. '즉시 중지'로 조회를 중단한 다음 자동 ON/OFF를 변경하세요."
                : "등록 작업이 진행 중입니다. 완료되거나 중지된 후 변경하세요.", "자동 설정 대기");
            return;
        }
        bool enable = shown.Any(job => !job.Enabled);
        int approved = 0;
        if (enable && shown.Any(job => job.RequiresReview))
            approved = ApproveImportedReviewsForBulk(shown);
        int eligible = shown.Count(job => !job.RequiresReview);
        int pending = shown.Count - eligible;
        if (enable && eligible == 0)
        {
            MessageBox.Show(this,
                $"조회한 {shown.Count}건 모두 '확인 필요' 상태입니다.\n" +
                "자동 실행을 켜려면 실제 사이트에서 해당 등록글의 상태와 금액을 먼저 확인해야 합니다.\n" +
                "이 체크박스는 검증되지 않은 글을 강제로 실행하지 않습니다.",
                "자동 실행 보류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string summary = enable
            ? $"조회 {shown.Count}건 중 {eligible}건 자동 ON\n확인 필요로 제외: {pending}건"
            : $"조회한 {shown.Count}건의 자동 실행을 모두 끕니다.";
        if (approved == 0 && MessageBox.Show(this, summary + "\n\n적용할까요?", "자동 일괄설정",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SaveSelectedJob(false);
        _applyingHeaderBulkSelection = true;
        try
        {
            foreach (var job in shown)
            {
                job.Enabled = enable && !job.RequiresReview;
                if (job.Enabled && job.NextDueUtc == DateTime.MaxValue)
                    job.NextDueUtc = DateTime.UtcNow;
            }
            _config.Jobs = _jobs.ToList();
            _config.Save();
        }
        finally { _applyingHeaderBulkSelection = false; }
        _jobsGrid.Refresh();
        _autoGrid.Refresh();
        RefreshAutoSummary();
        RefreshSelectAllHeaderVisuals();
        AppendLog($"표 상단 자동 {(enable ? "ON" : "OFF")}: 대상 {shown.Count}건 / 검토 필요 제외 {pending}건");
    }

    private void InstallSelectAllHeader(DataGridView grid)
    {
        var col = grid.Columns["BatchSelected"];
        if (col == null) return;
        col.HeaderText = "전체";
        col.Width = Math.Max(col.Width, 64);
        col.MinimumWidth = 64;
        col.SortMode = DataGridViewColumnSortMode.NotSortable;
        col.HeaderCell.ToolTipText = "클릭: 현재 조회된 물품 전체 선택 / 선택 해제";

        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex != -1 || e.ColumnIndex != col.Index) return;
            e.PaintBackground(e.CellBounds, false);
            e.Paint(e.CellBounds, DataGridViewPaintParts.Border);

            var visible = CurrentGridJobs(grid);
            int selectedCount = visible.Count(job => job.SelectedForBulk);
            var state = visible.Count > 0 && selectedCount == visible.Count
                ? CheckBoxState.CheckedNormal
                : selectedCount > 0 ? CheckBoxState.MixedNormal : CheckBoxState.UncheckedNormal;
            int y = e.CellBounds.Y + (e.CellBounds.Height - 14) / 2;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.CellBounds.Left + 5, y), state);
            var rect = new Rectangle(e.CellBounds.Left + 24, e.CellBounds.Top + 1,
                Math.Max(12, e.CellBounds.Width - 26), e.CellBounds.Height - 2);
            TextRenderer.DrawText(e.Graphics, "전체", e.CellStyle.Font ?? Font, rect,
                UiTheme.Text, TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        };
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex != col.Index) return;
            ToggleGridHeaderSelection(grid);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != col.Index || _applyingHeaderBulkSelection) return;
            RefreshSelectAllHeaderVisuals();
        };
        grid.DataBindingComplete += (_, _) => RefreshSelectAllHeaderVisuals();
    }

    private static List<ListingJob> CurrentGridJobs(DataGridView grid) =>
        grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow && row.Visible)
            .Select(row => row.DataBoundItem as ListingJob)
            .Where(job => job is not null)
            .Select(job => job!)
            .ToList();

    private void ToggleGridHeaderSelection(DataGridView grid)
    {
        if (_applyingHeaderBulkSelection) return;
        grid.EndEdit();
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var shown = CurrentGridJobs(grid);
        if (shown.Count == 0) return;
        // Click a fully checked header to clear; any mixed/unchecked header selects all.
        bool makeChecked = shown.Any(job => !job.SelectedForBulk);
        _applyingHeaderBulkSelection = true;
        try
        {
            foreach (var job in shown) job.SelectedForBulk = makeChecked;
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
        }
        finally { _applyingHeaderBulkSelection = false; }
        RefreshBatchSummary();
        RefreshSelectAllHeaderVisuals();
        AppendLog($"표 상단 전체선택: 현재 조회 {shown.Count}건 {(makeChecked ? "모두 선택" : "선택 해제")}. 다른 조회 조건의 물품은 변경하지 않음.");
    }

    private void RefreshSelectAllHeaderVisuals()
    {
        if (_jobsGrid.IsDisposed || _autoGrid.IsDisposed) return;
        foreach (var grid in new[] { _jobsGrid, _autoGrid })
        {
            if (!grid.IsHandleCreated) continue;
            foreach (string name in new[] { "BatchSelected", "Enabled" })
            {
                var col = grid.Columns[name];
                if (col != null) grid.InvalidateCell(col.HeaderCell);
            }
        }
    }
}
