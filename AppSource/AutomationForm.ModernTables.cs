using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private SplitContainer? _jobsSplit;
    private Label? _batchCountLabel;
    private bool _readingColumnLayout;
    private readonly System.Windows.Forms.Timer _saveColumnLayoutTimer = new() { Interval = 700 };
    private GridColumnPreferences _columnPreferences = new();
    private bool _wideGridEnabled;
    private bool _itemListFirstShown;
    private int _normalSplitDistance;

    private sealed class GridColumnPreferences
    {
        public GridColumnPreferences() { }
        public Dictionary<string, int> Jobs { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Auto { get; set; } = new(StringComparer.Ordinal);
    }

    private static string ColumnIdentity(DataGridViewColumn column) =>
        string.IsNullOrWhiteSpace(column.DataPropertyName) ? column.Name : column.DataPropertyName;

    private void InitializeModernGrids()
    {
        var path = Path.Combine(Settings.UserDirectory, "grid-columns-v17.json");
        try
        {
            if (File.Exists(path))
                _columnPreferences = JsonSerializer.Deserialize<GridColumnPreferences>(File.ReadAllText(path)) ?? new();
        }
        catch { _columnPreferences = new(); }
        _columnPreferences.Jobs ??= new();
        _columnPreferences.Auto ??= new();

        SetupGridStyle(_jobsGrid);
        SetupGridStyle(_autoGrid);
        // Keep critical business information visible without horizontal scrolling.
        ReorderColumns(_jobsGrid, "SelectedForBulk", "Enabled", "Operation", "ListingNumber",
            "Game", "Server", "QuantityDisplay", "Price", "Title");
        ReorderColumns(_autoGrid, "SelectedForBulk", "Enabled", "Operation", "Game",
            "Server", "Quantity", "Price", "Title", "NextDueLocal", "LastStatus");
        _readingColumnLayout = true;
        try
        {
            ApplySavedWidths(_jobsGrid, _columnPreferences.Jobs);
            ApplySavedWidths(_autoGrid, _columnPreferences.Auto);
        }
        finally { _readingColumnLayout = false; }

        _jobsGrid.ColumnWidthChanged += (_, _) => ScheduleColumnLayoutSave();
        _autoGrid.ColumnWidthChanged += (_, _) => ScheduleColumnLayoutSave();
        _saveColumnLayoutTimer.Tick += (_, _) =>
        {
            _saveColumnLayoutTimer.Stop();
            SaveGridColumnWidths();
        };
        FormClosing += (_, _) =>
        {
            _saveColumnLayoutTimer.Stop();
            SaveGridColumnWidths();
        };
    }

    private static void ReorderColumns(DataGridView grid, params string[] keys)
    {
        for (int target = 0; target < keys.Length; target++)
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (ColumnIdentity(col) != keys[target]) continue;
                col.DisplayIndex = target;
                break;
            }
        }
        // Two checkboxes remain visible while horizontally scrolling a wide table.
        foreach (DataGridViewColumn col in grid.Columns)
            if (col.DisplayIndex < 2) col.Frozen = true;
    }

    private void SetupGridStyle(DataGridView grid)
    {
        grid.AllowUserToResizeColumns = true;
        grid.AllowUserToOrderColumns = true;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 29;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = UiTheme.Border;
        grid.BackgroundColor = UiTheme.Surface;
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.SurfaceAlt;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = UiTheme.Surface;
        grid.DefaultCellStyle.ForeColor = UiTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Primary;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.Canvas;
        foreach (DataGridViewColumn col in grid.Columns)
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.Resizable = DataGridViewTriState.True;
            if (col.DataPropertyName is "Price" or "Quantity" or "QuantityDisplay" or "MinQuantity" or "MaxQuantity" or "IntervalMinutes")
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string field = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (field == "Price" && e.Value is not null &&
                long.TryParse(e.Value.ToString()?.Replace(",", ""), out long amount))
            {
                e.Value = amount.ToString("N0");
                e.FormattingApplied = true;
            }
            if (grid.Rows[e.RowIndex].DataBoundItem is not ListingJob job) return;
            if (field == "LastStatus")
            {
                e.Value = job.StatusDisplay;
                e.FormattingApplied = true;
            }
            if (field is "NextDueLocal" or "LastStatus")
            {
                if (job.RequiresReview)
                {
                    e.CellStyle.BackColor = UiTheme.WarningSurface;
                    e.CellStyle.ForeColor = UiTheme.Warning;
                }
                else if (job.Enabled && field == "LastStatus")
                {
                    e.CellStyle.ForeColor = UiTheme.Success;
                }
            }
        };
        grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                grid.Columns[e.ColumnIndex].DataPropertyName != "LastStatus") return;
            if (grid.Rows[e.RowIndex].DataBoundItem is ListingJob job)
                e.ToolTipText = job.LastStatus;
        };
    }

    private static void ApplySavedWidths(DataGridView grid, Dictionary<string, int> saved)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (saved.TryGetValue(ColumnIdentity(col), out int width))
                col.Width = Math.Clamp(width, Math.Max(col.MinimumWidth, 38), 1200);
        }
    }

    private void ScheduleColumnLayoutSave()
    {
        if (_readingColumnLayout || !IsHandleCreated) return;
        _saveColumnLayoutTimer.Stop();
        _saveColumnLayoutTimer.Start();
    }

    private void SaveGridColumnWidths()
    {
        if (_readingColumnLayout) return;
        try
        {
            var values = new GridColumnPreferences();
            foreach (DataGridViewColumn col in _jobsGrid.Columns)
                values.Jobs[ColumnIdentity(col)] = col.Width;
            foreach (DataGridViewColumn col in _autoGrid.Columns)
                values.Auto[ColumnIdentity(col)] = col.Width;
            Directory.CreateDirectory(Settings.UserDirectory);
            string path = Path.Combine(Settings.UserDirectory, "grid-columns-v17.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(values));
            File.Move(temp, path, true);
            _columnPreferences = values;
        }
        catch (Exception ex)
        {
            AppendLog("열 너비 설정 저장 실패: " + ex.Message);
        }
    }

    private void ResetGridColumns()
    {
        _readingColumnLayout = true;
        try
        {
            void Reset(DataGridView grid)
            {
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    col.Width = col.DataPropertyName switch
                    {
                        "SelectedForBulk" or "Enabled" => 53,
                        "Title" => 395,
                        "ListingNumber" => 150,
                        "Game" => 110,
                        "Server" => 100,
                        "Description" => 260,
                        "Price" => 112,
                        "LastStatus" => 160,
                        "NextDueLocal" => 138,
                        "IntervalMinutes" => 90,
                        _ => 95
                    };
                }
            }
            Reset(_jobsGrid);
            Reset(_autoGrid);
        }
        finally { _readingColumnLayout = false; }
        SaveGridColumnWidths();
        AppendLog("물품 목록 열 너비를 기본값으로 되돌렸습니다.");
    }

    private void ToggleWideGrid()
    {
        var split = _jobsSplit;
        if (split is null) return;
        if (!_wideGridEnabled)
        {
            _normalSplitDistance = split.SplitterDistance;
            split.Panel2Collapsed = true;
            _wideGridEnabled = true;
            AppendLog("표 넓게 보기: 우측 편집창 숨김. 물품 행을 더블클릭하면 편집창을 다시 엽니다.");
        }
        else
        {
            RestoreEditorPanel();
            AppendLog("표 넓게 보기 해제: 물품 편집창 표시");
        }
    }

    private void RestoreEditorPanel()
    {
        var split = _jobsSplit;
        if (split is null || !_wideGridEnabled) return;
        split.Panel2Collapsed = false;
        _wideGridEnabled = false;
        if (split.Width >= split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth)
        {
            int max = split.Width - split.Panel2MinSize - split.SplitterWidth;
            split.SplitterDistance = Math.Clamp(_normalSplitDistance, split.Panel1MinSize, max);
        }
    }
}
