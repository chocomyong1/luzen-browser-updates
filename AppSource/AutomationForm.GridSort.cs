using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private string _listingSortField = "";
    private bool _listingSortAscending = true;

    // BindingList<T> does not sort by itself. Sort the actual filtered jobs
    // and rebind both synchronized views while retaining checkbox selections.
    private void InstallListingHeaderSorting()
    {
        foreach (var grid in new[] { _autoGrid, _jobsGrid })
        {
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.DataPropertyName is "Server" or "Price")
                    column.SortMode = DataGridViewColumnSortMode.Programmatic;
            grid.ColumnHeaderMouseClick += (_, e) =>
            {
                // Rebinding during a live site submit could save a stale
                // editor and disturb the listing being independently verified.
                if (_sitePriceBatchRunning || (_busy && !_myListingsImportRunning)) return;
                if (e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
                var field = grid.Columns[e.ColumnIndex].DataPropertyName;
                if (field is not ("Server" or "Price")) return;
                var focused = grid.CurrentRow?.DataBoundItem as ListingJob;
                _listingSortAscending = _listingSortField == field ? !_listingSortAscending : true;
                _listingSortField = field;
                RefreshFilteredLists(focused?.Id);
                var matchingRow = grid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(row => (row.DataBoundItem as ListingJob)?.Id == focused?.Id);
                if (matchingRow is not null)
                {
                    grid.ClearSelection();
                    grid.CurrentCell = matchingRow.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
                    matchingRow.Selected = true;
                }
                AppendLog($"물품 정렬: {(field == "Price" ? "가격(숫자)" : "서버")} " +
                    (_listingSortAscending ? "오름차순" : "내림차순"));
            };
        }
    }

    private List<ListingJob> SortFilteredListingJobs(IEnumerable<ListingJob> source)
    {
        var rows = source.ToList();
        static long PriceOf(ListingJob job) => TrySitePrice(job.Price, out var value) ? value : 0;
        static int MissingPrice(ListingJob job) => TrySitePrice(job.Price, out _) ? 0 : 1;
        return _listingSortField switch
        {
            "Server" when _listingSortAscending => rows
                .OrderBy(job => job.Server ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(PriceOf).ThenBy(job => job.ListingNumber, StringComparer.Ordinal).ToList(),
            "Server" => rows
                .OrderByDescending(job => job.Server ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(PriceOf).ThenBy(job => job.ListingNumber, StringComparer.Ordinal).ToList(),
            "Price" when _listingSortAscending => rows
                .OrderBy(MissingPrice).ThenBy(PriceOf)
                .ThenBy(job => job.Server, StringComparer.CurrentCultureIgnoreCase).ToList(),
            "Price" => rows
                .OrderBy(MissingPrice).ThenByDescending(PriceOf)
                .ThenBy(job => job.Server, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => rows
        };
    }

    private void RefreshListingSortGlyphs()
    {
        foreach (var grid in new[] { _autoGrid, _jobsGrid })
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.DataPropertyName is not ("Server" or "Price")) continue;
                column.HeaderCell.SortGlyphDirection = column.DataPropertyName == _listingSortField
                    ? _listingSortAscending ? SortOrder.Ascending : SortOrder.Descending
                    : SortOrder.None;
            }
    }
}
