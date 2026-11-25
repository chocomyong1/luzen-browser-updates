using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private sealed class IntegratedRow
    {
        public required string System { get; init; }
        public required ListingJob Job { get; init; }
        public bool Selected { get; set; }
        public string Game => Job.Game;
        public string Server => Job.Server;
        public string SiteId => Job.SiteId;
        public string ListingNumber => Job.ListingNumber;
        public string Title => Job.Title;
        public string Quantity => Job.QuantityDisplay;
        public long PriceAmount => long.TryParse((Job.Price ?? "").Replace(",", ""), out var p) ? p : 0;
        public string Status => Job.StatusDisplay;
        public string TradeSide
        {
            get
            {
                if (Job.OriginalTradeSide is "구매" or "판매") return Job.OriginalTradeSide;
                if (Job.Operation == "구매등록") return "구매";
                if (Job.Operation == "판매등록") return "판매";
                if (MyListingsImport.IsOwnedListUrl(Job.SourceListUrl))
                    return MyListingsImport.IsSellListUrl(Job.SourceListUrl) ? "판매" : "구매";
                return "미확인";
            }
        }
    }

    private Panel? _integratedEditor;
    private DataGridView? _integratedGrid;
    private ComboBox? _integratedSystemFilter;
    private ComboBox? _integratedServerFilter;
    private TextBox? _integratedSearch;
    private Label? _integratedSummary;
    private Button? _integratedButton;
    private bool _integratedVisible;
    private readonly List<IntegratedRow> _integratedRows = new();
    private readonly System.Windows.Forms.Timer _integratedRefreshTimer = new() { Interval = 2000 };
    private bool _integratedTimerAttached;
    private string _integratedSort = "System";
    private bool _integratedAscending = true;
    private string _integratedTradeSide = "전체";
    private bool _integratedServerFilterUpdating;
    private readonly Dictionary<string, Button> _integratedTradeTabs = new();
    private Button? _integratedUndoButton;
    private List<(AutomationForm Form, BatchUndoToken Receipt)> _integratedLastUndo = new();

    private AutomationForm IntegratedOwner(string system) => system switch
    {
        "B" => _moduleB, "C" => _moduleC, _ => _moduleA
    };

    private static Button IntegratedButton(string text, int width, Action callback, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 35,
            Margin = new Padding(0, 3, 8, 0)
        };
        UiTheme.StyleButton(button, primary ? UiTheme.ButtonTone.Primary : UiTheme.ButtonTone.Neutral);
        button.Click += (_, _) => callback();
        return button;
    }

    private void ToggleIntegratedEditor()
    {
        if (_integratedVisible) { DisableIntegratedEditor(); return; }
        if (_marketVisible) DisableMarketView();
        if (_splitActive) DisableSplitView();
        EnableIntegratedEditor();
    }

    private void EnableIntegratedEditor()
    {
        if (_integratedVisible) return;
        // C's saved local jobs can be edited without creating its browser or
        // opening an unverified VPN route. No new WebView2 is instantiated.
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = UiTheme.Canvas, Padding = new Padding(14, 8, 14, 10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 87));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = UiTheme.Surface
        };
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        var title = new Label
        {
            Text = "A · B · C 통합 물품 편집", Dock = DockStyle.Left, Width = 265,
            Padding = new Padding(13, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        var close = IntegratedButton("단일 화면으로", 125, DisableIntegratedEditor);
        close.Anchor = AnchorStyles.Right;
        var tradeTabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            Padding = new Padding(0, 5, 0, 0)
        };
        foreach (string side in new[] { "전체", "구매", "판매" })
        {
            var tab = IntegratedButton(side, 73, () =>
            {
                _integratedTradeSide = side;
                RefreshIntegratedTradeTabs();
                RefreshIntegratedServerOptions();
                RefreshIntegratedGrid();
            });
            tab.Height = 32;
            _integratedTradeTabs[side] = tab;
            tradeTabs.Controls.Add(tab);
        }
        RefreshIntegratedTradeTabs();
        titleBar.Controls.Add(title, 0, 0);
        titleBar.Controls.Add(tradeTabs, 1, 0);
        titleBar.Controls.Add(close, 2, 0);
        layout.Controls.Add(titleBar, 0, 0);

        var toolsArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = UiTheme.Surface, Padding = new Padding(12, 1, 8, 0)
        };
        toolsArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        toolsArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        filters.Controls.Add(new Label
        {
            Text = "시스템", AutoSize = true, Padding = new Padding(2, 12, 5, 0), ForeColor = UiTheme.Muted
        });
        _integratedSystemFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 112,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(0, 7, 14, 0)
        };
        _integratedSystemFilter.Items.AddRange(new object[] { "전체", "A", "B", "C" });
        _integratedSystemFilter.SelectedIndex = 0;
        _integratedSystemFilter.SelectedIndexChanged += (_, _) =>
        {
            RefreshIntegratedServerOptions();
            RefreshIntegratedGrid();
        };
        filters.Controls.Add(_integratedSystemFilter);
        filters.Controls.Add(new Label
        {
            Text = "서버", AutoSize = true, Padding = new Padding(2, 12, 5, 0), ForeColor = UiTheme.Muted
        });
        _integratedServerFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 145,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(0, 7, 14, 0)
        };
        _integratedServerFilter.Items.Add("전체");
        _integratedServerFilter.SelectedIndex = 0;
        _integratedServerFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_integratedServerFilterUpdating) RefreshIntegratedGrid();
        };
        filters.Controls.Add(_integratedServerFilter);
        _integratedSearch = new TextBox
        {
            Width = 266, Height = 30, PlaceholderText = "제목 · 사이트 ID · 등록번호 검색",
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(0, 7, 12, 0)
        };
        _integratedSearch.TextChanged += (_, _) => RefreshIntegratedGrid();
        filters.Controls.Add(_integratedSearch);
        filters.Controls.Add(IntegratedButton("목록 새로고침", 113, RebuildIntegratedRows));
        _integratedSummary = new Label
        {
            AutoSize = true, Padding = new Padding(8, 12, 0, 0), ForeColor = UiTheme.Muted
        };
        filters.Controls.Add(_integratedSummary);
        toolsArea.Controls.Add(filters, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        actions.Controls.Add(IntegratedButton("현재 목록 전체 선택", 145, () => MarkIntegratedVisible(true)));
        actions.Controls.Add(IntegratedButton("같은 가격 선택", 126, SelectIntegratedSamePrice));
        actions.Controls.Add(IntegratedButton("선택 해제", 95, () => MarkIntegratedVisible(false)));
        actions.Controls.Add(IntegratedButton("선택 일괄수정", 134, ShowIntegratedBulkDialog, primary: true));
        actions.Controls.Add(IntegratedButton("저장 · 가격 순차 반영", 177,
            () => _ = SaveIntegratedCheckedPricesAsync(), primary: true));
        actions.Controls.Add(IntegratedButton("가격 예약 현황", 125, ShowPriceReservationsDialog));
        _integratedUndoButton = IntegratedButton("마지막 수정 되돌리기", 161, UndoIntegratedBatchEdit);
        _integratedUndoButton.Visible = _integratedLastUndo.Count > 0;
        actions.Controls.Add(_integratedUndoButton);
        actions.Controls.Add(new Label
        {
            Text = "※ 가격 저장 시 A→B→C 순차 반영 · 각 등록번호의 실제 가격 확인 후 다음 글 진행",
            AutoSize = true, Padding = new Padding(10, 12, 0, 0), ForeColor = UiTheme.Muted
        });
        toolsArea.Controls.Add(actions, 0, 1);
        layout.Controls.Add(toolsArea, 0, 1);

        _integratedGrid = new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, MultiSelect = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiTheme.Border,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
                SelectionBackColor = UiTheme.SurfaceAlt, SelectionForeColor = UiTheme.Text,
                Font = new Font("Segoe UI", 9.2F, FontStyle.Bold)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.Surface, ForeColor = UiTheme.Text,
                SelectionBackColor = Color.FromArgb(38, 83, 77),
                SelectionForeColor = UiTheme.Text,
                Font = new Font("Segoe UI", 9.2F)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text,
                SelectionBackColor = Color.FromArgb(38, 83, 77),
                SelectionForeColor = UiTheme.Text
            },
            AllowUserToResizeRows = false, ColumnHeadersHeight = 37,
            RowTemplate = { Height = 30 }
        };
        DarkScrollbarTheme.Attach(_integratedGrid);
        _integratedGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected", HeaderText = "선택", DataPropertyName = "Selected", Width = 54,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        void Column(string key, string name, int width)
        {
            _integratedGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = key, DataPropertyName = key, HeaderText = name, Width = width,
                ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic
            });
        }
        Column("System", "시스템", 73);
        Column("TradeSide", "거래", 67);
        Column("Game", "게임", 98);
        Column("Server", "서버", 109);
        Column("Quantity", "수량", 65);
        Column("PriceAmount", "가격(원)", 107);
        _integratedGrid.Columns["PriceAmount"].DefaultCellStyle.Format = "N0";
        _integratedGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Title", HeaderText = "물품 제목", DataPropertyName = "Title",
            MinimumWidth = 170, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic
        });
        Column("SiteId", "사이트 ID", 110);
        Column("ListingNumber", "등록번호", 130);
        Column("Status", "상태", 137);
        _integratedGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_integratedGrid.IsCurrentCellDirty)
                _integratedGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _integratedGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _integratedGrid.Columns[e.ColumnIndex].Name == "Selected")
                UpdateIntegratedSummary();
        };
        _integratedGrid.ColumnHeaderMouseClick += (_, e) =>
        {
            string key = _integratedGrid.Columns[e.ColumnIndex].Name;
            if (key == "Selected") return;
            _integratedAscending = _integratedSort != key || !_integratedAscending;
            _integratedSort = key;
            RefreshIntegratedGrid();
        };
        _integratedGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _integratedGrid.Rows[e.RowIndex].DataBoundItem is not IntegratedRow row) return;
            _ = OpenIntegratedJobAsync(row);
        };
        _integratedGrid.DataError += (_, e) => e.ThrowException = false;
        layout.Controls.Add(_integratedGrid, 0, 2);
        panel.Controls.Add(layout);
        _integratedEditor = panel;
        _workspaceBody.Controls.Add(panel);
        panel.BringToFront();
        _integratedVisible = true;
        if (_integratedButton is not null) _integratedButton.Text = "통합 닫기";
        if (!_integratedTimerAttached)
        {
            _integratedRefreshTimer.Tick += (_, _) =>
            {
                if (!_integratedVisible) return;
                _integratedGrid?.Refresh();
                UpdateIntegratedSummary();
            };
            _integratedTimerAttached = true;
        }
        _integratedRefreshTimer.Start();
        RebuildIntegratedRows();
        _status.Text = "  A/B/C 통합 편집 · 체크한 물품만 적용 · 자동등록은 백그라운드에서 계속 진행";
    }

    private void DisableIntegratedEditor()
    {
        if (!_integratedVisible) return;
        _integratedRefreshTimer.Stop();
        _integratedVisible = false;
        _integratedRows.Clear();
        if (_integratedEditor is not null)
        {
            _workspaceBody.Controls.Remove(_integratedEditor);
            _integratedEditor.Dispose();
        }
        _integratedEditor = null;
        _integratedGrid = null;
        _integratedSystemFilter = null;
        _integratedServerFilter = null;
        _integratedSearch = null;
        _integratedSummary = null;
        _integratedUndoButton = null;
        _integratedTradeTabs.Clear();
        _integratedTradeSide = "전체";
        if (_integratedButton is not null) _integratedButton.Text = "통합 편집";
        _module.BringToFront();
    }

    private void RebuildIntegratedRows()
    {
        _integratedGrid?.EndEdit();
        var marked = new HashSet<ListingJob>(_integratedRows.Where(row => row.Selected).Select(row => row.Job));
        _integratedRows.Clear();
        foreach (string system in new[] { "A", "B", "C" })
            foreach (var job in IntegratedOwner(system).IntegratedEditJobs)
                _integratedRows.Add(new IntegratedRow { System = system, Job = job, Selected = marked.Contains(job) });
        RefreshIntegratedServerOptions();
        RefreshIntegratedGrid();
    }

    private void RefreshIntegratedTradeTabs()
    {
        foreach (var tab in _integratedTradeTabs)
            UiTheme.StyleButton(tab.Value, tab.Key == _integratedTradeSide
                ? UiTheme.ButtonTone.Primary : UiTheme.ButtonTone.Neutral);
    }

    private void RefreshIntegratedServerOptions()
    {
        if (_integratedServerFilter is null) return;
        string previous = _integratedServerFilter.SelectedItem?.ToString() ?? "전체";
        string system = _integratedSystemFilter?.SelectedItem?.ToString() ?? "전체";
        var servers = _integratedRows
            .Where(row => (system == "전체" || row.System == system) &&
                          (_integratedTradeSide == "전체" || row.TradeSide == _integratedTradeSide))
            .Select(row => row.Server).Where(server => !string.IsNullOrWhiteSpace(server))
            .Distinct(StringComparer.CurrentCulture).OrderBy(server => server, StringComparer.CurrentCulture)
            .ToList();
        _integratedServerFilterUpdating = true;
        try
        {
            _integratedServerFilter.Items.Clear();
            _integratedServerFilter.Items.Add("전체");
            foreach (var server in servers) _integratedServerFilter.Items.Add(server);
            _integratedServerFilter.SelectedItem = servers.Contains(previous) ? previous : "전체";
        }
        finally { _integratedServerFilterUpdating = false; }
    }

    private List<IntegratedRow> VisibleIntegratedRows()
    {
        string system = _integratedSystemFilter?.SelectedItem?.ToString() ?? "전체";
        string server = _integratedServerFilter?.SelectedItem?.ToString() ?? "전체";
        string search = (_integratedSearch?.Text ?? "").Trim();
        return _integratedRows.Where(row => (system == "전체" || row.System == system) &&
            (_integratedTradeSide == "전체" || row.TradeSide == _integratedTradeSide) &&
            (server == "전체" || row.Server == server) &&
            (search.Length == 0 || new[] { row.Server, row.Game, row.Title, row.SiteId, row.ListingNumber }
                .Any(value => (value ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    private void RefreshIntegratedGrid()
    {
        if (_integratedGrid is null || _integratedGrid.IsDisposed) return;
        _integratedGrid.EndEdit();
        var visible = VisibleIntegratedRows();
        Func<IntegratedRow, object> key = _integratedSort switch
        {
            "PriceAmount" => row => row.PriceAmount,
            "System" => row => row.System,
            "TradeSide" => row => row.TradeSide,
            "Server" => row => row.Server,
            "Game" => row => row.Game,
            "Quantity" => row => row.Quantity,
            "Title" => row => row.Title,
            "SiteId" => row => row.SiteId,
            "ListingNumber" => row => row.ListingNumber,
            "Status" => row => row.Status,
            _ => row => row.System
        };
        var ordered = _integratedAscending ? visible.OrderBy(key).ToList() : visible.OrderByDescending(key).ToList();
        _integratedGrid.DataSource = new BindingList<IntegratedRow>(ordered);
        UpdateIntegratedSummary();
    }

    private void UpdateIntegratedSummary()
    {
        if (_integratedSummary is null || _integratedGrid is null) return;
        var visible = VisibleIntegratedRows();
        int selected = visible.Count(row => row.Selected);
        _integratedSummary.Text = $"조회 {visible.Count} / 전체 {_integratedRows.Count} · 선택 {selected}건";
    }

    private void MarkIntegratedVisible(bool marked)
    {
        _integratedGrid?.EndEdit();
        foreach (var row in VisibleIntegratedRows()) row.Selected = marked;
        _integratedGrid?.Refresh();
        UpdateIntegratedSummary();
    }

    private void SelectIntegratedSamePrice()
    {
        _integratedGrid?.EndEdit();
        if (_integratedGrid?.CurrentRow?.DataBoundItem is not IntegratedRow reference || reference.PriceAmount <= 0)
        {
            MessageBox.Show(this, "기준 가격으로 삼을 물품 한 줄을 먼저 선택하세요.", "같은 가격 선택");
            return;
        }
        var visible = VisibleIntegratedRows();
        foreach (var row in visible) row.Selected = row.PriceAmount == reference.PriceAmount;
        _integratedGrid.Refresh();
        UpdateIntegratedSummary();
        _status.Text = $"  {reference.PriceAmount:N0}원 물품 {visible.Count(row => row.Selected)}건 선택 · A/B/C 구분 유지";
    }

    private void ShowIntegratedBulkDialog()
    {
        _integratedGrid?.EndEdit();
        var visible = VisibleIntegratedRows();
        var targets = visible.Where(row => row.Selected).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "먼저 수정할 물품을 체크하세요. 현재 검색 결과에 표시된 물품만 적용됩니다.", "선택 필요");
            return;
        }
        using var dialog = new Form
        {
            Text = "A/B/C 선택 물품 일괄수정", Size = new Size(560, 330),
            MinimumSize = new Size(530, 330), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = UiTheme.Surface, Font = Font
        };
        var counts = string.Join(" · ", targets.GroupBy(row => row.System).OrderBy(g => g.Key)
            .Select(group => $"{group.Key} {group.Count()}건"));
        dialog.Controls.Add(new Label
        {
            Text = $"선택 {targets.Count}건  |  {counts}", Dock = DockStyle.Top, Height = 53,
            Padding = new Padding(20, 14, 0, 0), Font = new Font(Font, FontStyle.Bold)
        });
        var fields = new (string Label, IntegratedEditField Field)[]
        {
            ("가격 (원)", IntegratedEditField.Price),
            ("수량", IntegratedEditField.Quantity),
            ("물품 제목", IntegratedEditField.Title),
            ("상세 설명", IntegratedEditField.Description),
            ("캐릭터명", IntegratedEditField.Character),
            ("재등록 주기 (분)", IntegratedEditField.IntervalMinutes),
            ("자동실행 ON/OFF", IntegratedEditField.Enabled)
        };
        var fieldLabel = new Label { Text = "변경 항목", Left = 23, Top = 70, Width = 95 };
        var fieldInput = new ComboBox
        {
            Left = 128, Top = 67, Width = 382, DropDownStyle = ComboBoxStyle.DropDownList
        };
        fieldInput.Items.AddRange(fields.Select(item => item.Label).Cast<object>().ToArray());
        fieldInput.SelectedIndex = 0;
        var valueLabel = new Label { Text = "새 값", Left = 23, Top = 115, Width = 95 };
        var valueInput = new TextBox { Left = 128, Top = 112, Width = 382, PlaceholderText = "예: 93,000" };
        var note = new Label
        {
            Left = 23, Top = 160, Width = 489, Height = 69,
            Text = "기존 본인 구매글은 [저장]을 누르면 A→B→C 순서로 사이트 가격을 반영합니다.\n" +
                   "신규글은 첫 등록 가격으로 저장하고, 사이트 실패 시 다음 글은 제출하지 않습니다.",
            ForeColor = UiTheme.Muted
        };
        fieldInput.SelectedIndexChanged += (_, _) =>
        {
            var field = fields[fieldInput.SelectedIndex].Field;
            note.Text = field == IntegratedEditField.Price
                ? "기존 본인 구매글은 [저장]을 누르면 A→B→C 순서로 사이트 가격을 반영합니다.\n" +
                  "신규글은 첫 등록 가격으로 저장하고, 사이트 실패 시 다음 글은 제출하지 않습니다."
                : "선택한 물품의 로컬 설정만 변경합니다. 사이트에 즉시 제출하지 않습니다.\n" +
                  "자동등록 진행 중인 물품은 현재 작업 완료 후 적용합니다.";
            valueInput.PlaceholderText = field switch
            {
                IntegratedEditField.Price => "예: 93,000",
                IntegratedEditField.Quantity => "예: 3",
                IntegratedEditField.IntervalMinutes => "예: 30",
                IntegratedEditField.Enabled => "ON 또는 OFF",
                _ => "선택한 물품에 적용할 값"
            };
        };
        var cancel = new Button { Text = "취소", Left = 290, Top = 241, Width = 98, Height = 36 };
        var apply = new Button { Text = "저장", Left = 401, Top = 241, Width = 109, Height = 36 };
        UiTheme.StyleButton(cancel);
        UiTheme.StyleButton(apply, UiTheme.ButtonTone.Primary);
        cancel.Click += (_, _) => dialog.Close();
        apply.Click += async (_, _) =>
        {
            var field = fields[fieldInput.SelectedIndex].Field;
            string value = valueInput.Text.Trim();
            if (field == IntegratedEditField.Price) value = value.Replace(",", "").Replace("원", "");
            if (field == IntegratedEditField.Enabled) value = value.ToUpperInvariant();
            var command = new IntegratedEditCommand(field, value);
            var groups = targets.GroupBy(row => row.System).OrderBy(group => group.Key)
                .Select(group => (System: group.Key, Form: IntegratedOwner(group.Key),
                    Jobs: (IReadOnlyList<ListingJob>)group.Select(row => row.Job).ToList())).ToList();
            foreach (var group in groups)
            {
                if (group.Form.ValidateIntegratedEdit(group.Jobs, command, out var problem)) continue;
                MessageBox.Show(dialog, $"{group.System} 시스템: {problem}\n어느 시스템에도 변경하지 않았습니다.",
                    "일괄수정 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(dialog,
                $"{counts}에 '{fields[fieldInput.SelectedIndex].Label}'을(를) 적용할까요?\n\n" +
                (field == IntegratedEditField.Price
                    ? "기존 구매글은 저장 직후 A→B→C 순서로 사이트 가격 수정을 시도합니다. 각 글의 실제 가격 확인 후 다음으로 진행하며 실패하면 중단합니다."
                    : "사이트의 기존 게시글은 즉시 바꾸지 않습니다. 로컬 목록만 변경합니다.") +
                "\n\nA/B/C의 작업 순번과 시간당 제한은 그대로 유지됩니다.",
                "통합 일괄수정", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var saved = new List<string>();
            var receipts = new List<(AutomationForm Form, BatchUndoToken Receipt)>();
            int immediate = 0, deferred = 0;
            foreach (var group in groups)
            {
                try
                {
                    var result = group.Form.ApplyIntegratedEdit(group.Jobs, command);
                    immediate += result.Saved;
                    deferred += result.Deferred;
                    saved.Add(group.System);
                    receipts.Add((group.Form, result.UndoToken));
                }
                catch (Exception ex)
                {
                    _integratedLastUndo = receipts;
                    if (_integratedUndoButton is not null)
                        _integratedUndoButton.Visible = receipts.Count > 0;
                    MessageBox.Show(dialog, $"{group.System} 설정 저장 실패: {ex.Message}\n" +
                        (saved.Count > 0 ? "이미 저장된 시스템: " + string.Join(", ", saved) : "변경 완료된 시스템 없음") +
                        "\n다른 시스템에는 더 이상 적용하지 않았습니다.",
                        "부분 저장 확인", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RebuildIntegratedRows();
                    return;
                }
            }
            _integratedLastUndo = receipts;
            if (_integratedUndoButton is not null) _integratedUndoButton.Visible = true;
            RebuildIntegratedRows();
            _status.Text = $"  A/B/C 통합 수정 완료 · {immediate}건 저장" +
                (deferred > 0 ? $" · 실행 중 {deferred}건은 작업 종료 후 적용" : "") +
                (field == IntegratedEditField.Price ? " · 기존글 사이트 순차 반영 시작" : " · 사이트 즉시 제출 없음");
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
            if (field == IntegratedEditField.Price)
            {
                // Local undo is not a reversal of a submitted site price.
                _integratedLastUndo.Clear();
                if (_integratedUndoButton is not null) _integratedUndoButton.Visible = false;
                await ApplyIntegratedSavedPricesAsync(targets);
            }
        };
        dialog.Controls.Add(fieldLabel); dialog.Controls.Add(fieldInput);
        dialog.Controls.Add(valueLabel); dialog.Controls.Add(valueInput);
        dialog.Controls.Add(note);
        dialog.Controls.Add(cancel); dialog.Controls.Add(apply);
        UiTheme.ApplyControlStyles(dialog);
        dialog.ShowDialog(this);
    }

    private void UndoIntegratedBatchEdit()
    {
        if (_integratedLastUndo.Count == 0) return;
        foreach (var (form, receipt) in _integratedLastUndo)
        {
            if (form.CanUndoBatchEdit(receipt, out var reason)) continue;
            MessageBox.Show(this, reason + "\nA/B/C의 다른 변경을 보호하기 위해 어느 시스템도 되돌리지 않았습니다.",
                "통합 수정 되돌리기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int count = _integratedLastUndo.Sum(pair => pair.Receipt.Changes.Count);
        if (MessageBox.Show(this,
            $"마지막 통합 일괄수정 {count}건의 로컬 변경을 되돌릴까요?\n\n" +
            "사이트에 이미 제출된 변경은 복구하지 않습니다.",
            "통합 수정 되돌리기", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        foreach (var (form, receipt) in _integratedLastUndo)
        {
            if (form.TryUndoBatchEdit(receipt, out var reason)) continue;
            MessageBox.Show(this, reason + "\n다른 시스템의 변경 상태도 확인하세요.",
                "통합 수정 일부 복구 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RebuildIntegratedRows();
            return;
        }
        _integratedLastUndo.Clear();
        if (_integratedUndoButton is not null) _integratedUndoButton.Visible = false;
        RebuildIntegratedRows();
        _status.Text = $"  마지막 통합 일괄수정 {count}건의 로컬 값 복구 완료 · 사이트 제출은 변경하지 않음";
    }

    private async Task OpenIntegratedJobAsync(IntegratedRow row)
    {
        if (row.System is ("B" or "C") && !DualSystemNetwork.IsRouteVerified(row.System))
        {
            // Do not show or initialize B/C WebView2 on an unverified route.
            await SelectSystemAsync(row.System);
            if (!DualSystemNetwork.IsRouteVerified(row.System))
            {
                if (_integratedEditor is not null) _integratedEditor.BringToFront();
                return;
            }
        }
        DisableIntegratedEditor();
        if (_splitActive) DisableSplitView();
        await SelectSystemAsync(row.System);
        IntegratedOwner(row.System).OpenIntegratedJob(row.Job.Id);
    }
}
