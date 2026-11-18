using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private ListingJob? _batchContextTarget;
    private DataGridView? _batchContextGrid;

    // Both the automation-status grid and the editing grid use the same local
    // list and checkbox model. No website actions are triggered by this menu.
    private void InitializeBatchContextMenus()
    {
        AttachBatchContextMenu(_jobsGrid);
        AttachBatchContextMenu(_autoGrid);
    }

    private void AttachBatchContextMenu(DataGridView grid)
    {
        var menu = new ContextMenuStrip();
        grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _batchContextGrid = grid;
            var hit = grid.HitTest(e.X, e.Y);
            _batchContextTarget = hit.RowIndex >= 0 && hit.RowIndex < grid.Rows.Count
                ? grid.Rows[hit.RowIndex].DataBoundItem as ListingJob : null;
            if (_batchContextTarget is null) return;
            // Indicate the reference row visually; do not alter the bulk checkbox.
            if (hit.RowIndex >= 0 && grid.Rows[hit.RowIndex].Cells.Count > 0)
            {
                var candidate = grid.Rows[hit.RowIndex].Cells.Cast<DataGridViewCell>()
                    .FirstOrDefault(c => c.Visible && !c.ReadOnly);
                candidate ??= grid.Rows[hit.RowIndex].Cells.Cast<DataGridViewCell>()
                    .FirstOrDefault(c => c.Visible);
                if (candidate is not null) grid.CurrentCell = candidate;
            }
        };
        menu.Opening += (_, e) =>
        {
            if (_batchContextTarget is not ListingJob target || !_filteredJobs.Contains(target))
            {
                e.Cancel = true;
                return;
            }
            FillBatchContextMenu(menu, target);
        };
        grid.ContextMenuStrip = menu;
    }

    private void FillBatchContextMenu(ContextMenuStrip menu, ListingJob target)
    {
        menu.Items.Clear();
        var select = new ToolStripMenuItem("선택");
        menu.Items.Add(select);
        foreach (var (caption, scope) in new[]
        {
            ("전체물품 중", BatchScope.All),
            ("판매물품 중", BatchScope.Sell),
            ("구매물품 중", BatchScope.Buy)
        })
        {
            var group = new ToolStripMenuItem(caption)
            {
                ToolTipText = "현재 조회된 물품만 대상으로 선택합니다."
            };
            select.DropDownItems.Add(group);
            AddCriterion(group, "모두 선택", target, scope, BatchCriterion.All);
            group.DropDownItems.Add(new ToolStripSeparator());
            AddCriterion(group, "동일한 서브 ID", target, scope, BatchCriterion.SubId);
            AddCriterion(group, "동일한 아이디(사이트 ID)", target, scope, BatchCriterion.SiteId);
            AddCriterion(group, "동일한 게임", target, scope, BatchCriterion.Game);
            AddCriterion(group, "동일한 서버", target, scope, BatchCriterion.Server);
            AddCriterion(group, "동일한 물품구분", target, scope, BatchCriterion.Category);
            AddCriterion(group, "동일한 제목", target, scope, BatchCriterion.Title);
            AddCriterion(group, "동일한 금액", target, scope, BatchCriterion.Price);
        }
        select.DropDownItems.Add(new ToolStripSeparator());
        select.DropDownItems.Add("위로 선택", null, (_, _) => SelectRowsRelativeTo(target, above: true));
        select.DropDownItems.Add("아래로 선택", null, (_, _) => SelectRowsRelativeTo(target, above: false));

        menu.Items.Add(new ToolStripMenuItem("같은 금액 모두 선택", null,
            (_, _) => SelectByCriterion(target, BatchScope.All, BatchCriterion.Price, "같은 금액")));
        menu.Items.Add(new ToolStripMenuItem("조회 결과 전체선택", null,
            (_, _) => ApplyContextBatchSelection(_filteredJobs, "조회 결과 전체선택")));
        menu.Items.Add(new ToolStripMenuItem("조회 결과 선택 해제", null,
            (_, _) => SetVisibleBatchSelection(false)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("선택한 물품 일괄수정", null, (_, _) => BulkEditSelected()));
        if (ReferenceEquals(_batchContextGrid, _autoGrid))
        {
            // Advanced table columns remain available without occupying a
            // permanent toolbar on the registration-status page.
            menu.Items.Add(new ToolStripSeparator());
            bool visible = _autoGrid.Columns.Cast<DataGridViewColumn>()
                .Any(c => c.DataPropertyName == "MinQuantity" && c.Visible);
            menu.Items.Add(visible ? "최소·최대·주기 열 숨기기" : "최소·최대·주기 열 표시", null,
                (_, _) =>
                {
                    foreach (DataGridViewColumn column in _autoGrid.Columns)
                        if (column.DataPropertyName is "MinQuantity" or "MaxQuantity" or "IntervalMinutes")
                            column.Visible = !visible;
                });
        }
    }

    private void AddCriterion(ToolStripMenuItem group, string title, ListingJob target,
        BatchScope scope, BatchCriterion criterion)
    {
        group.DropDownItems.Add(title, null,
            (_, _) => SelectByCriterion(target, scope, criterion, $"{group.Text} · {title}"));
    }

    private void SelectByCriterion(ListingJob target, BatchScope scope,
        BatchCriterion criterion, string caption)
    {
        if (!BatchSelectionRules.IsValidAnchor(target, criterion))
        {
            MessageBox.Show(this, criterion == BatchCriterion.Price
                ? "기준 물품의 가격이 비어 있거나 올바르지 않습니다. 가격을 입력한 뒤 다시 선택하세요."
                : "기준 물품의 선택 조건이 비어 있어 비교할 수 없습니다.",
                "일괄선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var matches = BatchSelectionRules.Select(_filteredJobs, target, scope, criterion);
        ApplyContextBatchSelection(matches, caption);
    }

    private void SelectRowsRelativeTo(ListingJob target, bool above)
    {
        // Respect the user's CURRENT screen order (including header sorting),
        // not the backing BindingList order which may differ after sorting.
        var shown = (_batchContextGrid ?? _jobsGrid).Rows.Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem as ListingJob).Where(job => job is not null)
            .Select(job => job!).ToList();
        int index = shown.IndexOf(target);
        if (index < 0) return;
        // Includes the right-clicked row, limited to the current search result.
        var selected = shown.Where((_, i) => above ? i <= index : i >= index).ToList();
        ApplyContextBatchSelection(selected, above ? "위로 선택" : "아래로 선택");
    }

    private void ApplyContextBatchSelection(IEnumerable<ListingJob> matches, string caption)
    {
        // Replace checked rows only within the current search result. Jobs
        // hidden by filters are untouched and are excluded from bulk editing.
        var ids = matches.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var job in _filteredJobs) job.SelectedForBulk = ids.Contains(job.Id);
        _jobsGrid.Refresh();
        _autoGrid.Refresh();
        RefreshBatchSummary();
        AppendLog($"우클릭 일괄선택: {caption} → {ids.Count}건 (현재 조회 결과만)");
    }
}
