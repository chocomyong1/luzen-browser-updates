using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private MarketBridgeServer? _marketBridge;
    private MarketBridgeSnapshot? _marketSnapshot;
    private DateTime _marketReceivedUtc = DateTime.MinValue;
    private string _marketBridgeError = "";
    private bool _marketVisible;
    private Panel? _marketView;
    private DataGridView? _marketGrid;
    private Label? _marketSummary;
    private Label? _marketConnection;
    private ComboBox? _marketServerFilter;
    private ComboBox? _marketLevelFilter;
    private Button? _marketButton;
    private readonly System.Windows.Forms.Timer _marketStatusTimer = new() { Interval = 10000 };

    private void StartMarketBridge()
    {
        try
        {
            string localToken = MarketBridgeServer.LoadOrCreateToken(
                Path.Combine(Settings.UserDirectory, "vanta-market-bridge-token.txt"));
            _marketBridge = new MarketBridgeServer(localToken);
            _marketBridge.SnapshotReceived += snapshot =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || Disposing) return;
                        if (_marketSnapshot is not null && snapshot.ObservedAt < _marketSnapshot.ObservedAt)
                            return;
                        _marketSnapshot = snapshot;
                        _marketReceivedUtc = DateTime.UtcNow;
                        RefreshMarketFilters();
                        RefreshMarketGrid();
                    }));
                }
                catch (InvalidOperationException) { } // Window closing.
            };
            _marketBridge.Start();
            _marketStatusTimer.Tick += (_, _) => RefreshMarketStatus();
            _marketStatusTimer.Start();
        }
        catch (Exception ex)
        {
            _marketBridgeError = ex.GetType().Name;
            _marketBridge?.Dispose();
            _marketBridge = null;
        }
    }

    private void ToggleMarketView()
    {
        if (_marketVisible) { DisableMarketView(); return; }
        if (_splitActive) DisableSplitView();
        if (_integratedVisible) DisableIntegratedEditor();
        EnableMarketView();
    }

    private void EnableMarketView()
    {
        if (_marketVisible) return;
        if (_marketView is null) BuildMarketView();
        _marketVisible = true;
        _marketView!.Visible = true;
        _marketView.BringToFront();
        if (_marketButton is not null) _marketButton.Text = "시세 닫기";
        RefreshMarketFilters();
        RefreshMarketGrid();
        RefreshSystemLabel();
        _status.Text = "  시세 분석 · Firefox Gem Tracker의 검증된 구매 시세만 표시 · 자동 가격 변경 없음";
    }

    private void DisableMarketView()
    {
        if (!_marketVisible) return;
        _marketVisible = false;
        if (_marketView is not null) _marketView.Visible = false;
        if (_marketButton is not null) _marketButton.Text = "시세 분석";
        _module.BringToFront();
        RefreshSystemLabel();
        _status.Text = $"  시스템 {_activeSystem} 자동등록 현황 · 시세 추적기는 Firefox에서 별도 실행됩니다.";
    }

    private void BuildMarketView()
    {
        var view = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = UiTheme.Canvas, Padding = new Padding(14, 9, 14, 11)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, BackColor = UiTheme.Surface
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        heading.Controls.Add(new Label
        {
            Text = "로스트아크 보석 시세 분석", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0),
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), ForeColor = UiTheme.Text
        }, 0, 0);
        _marketConnection = new Label
        {
            Text = "추적기 연결 대기", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted
        };
        heading.Controls.Add(_marketConnection, 1, 0);
        var close = IntegratedButton("단일 화면으로", 129, DisableMarketView);
        close.Anchor = AnchorStyles.Right;
        heading.Controls.Add(close, 2, 0);
        layout.Controls.Add(heading, 0, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            BackColor = UiTheme.Surface, Padding = new Padding(12, 7, 0, 0)
        };
        filters.Controls.Add(new Label { Text = "서버", AutoSize = true,
            Padding = new Padding(1, 9, 4, 0), ForeColor = UiTheme.Muted });
        _marketServerFilter = new ComboBox
        {
            Width = 156, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(0, 4, 13, 0)
        };
        _marketServerFilter.Items.Add("전체");
        _marketServerFilter.SelectedIndex = 0;
        _marketServerFilter.SelectedIndexChanged += (_, _) => RefreshMarketGrid();
        filters.Controls.Add(_marketServerFilter);
        filters.Controls.Add(new Label { Text = "레벨", AutoSize = true,
            Padding = new Padding(0, 9, 4, 0), ForeColor = UiTheme.Muted });
        _marketLevelFilter = new ComboBox
        {
            Width = 110, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(0, 4, 13, 0)
        };
        _marketLevelFilter.Items.AddRange(new object[] { "전체", "8레벨", "9레벨", "10레벨" });
        _marketLevelFilter.SelectedIndex = 0;
        _marketLevelFilter.SelectedIndexChanged += (_, _) => RefreshMarketGrid();
        filters.Controls.Add(_marketLevelFilter);
        filters.Controls.Add(new Label { Text = "● 추적기 자동 연결", AutoSize = true,
            Padding = new Padding(8, 9, 12, 0), ForeColor = UiTheme.Success });
        filters.Controls.Add(IntegratedButton("통합 편집 열기", 143, () =>
        {
            DisableMarketView();
            EnableIntegratedEditor();
        }));
        _marketSummary = new Label
        {
            Text = "Gem Tracker를 연결하면 경쟁 구매글 시세가 여기에 표시됩니다.",
            Width = 1070, Height = 29, Padding = new Padding(4, 6, 0, 0),
            ForeColor = UiTheme.Muted
        };
        var filterBlock = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Surface
        };
        filterBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        filterBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filterBlock.Controls.Add(filters, 0, 0);
        filterBlock.Controls.Add(_marketSummary, 0, 1);
        layout.Controls.Add(filterBlock, 0, 1);

        _marketGrid = new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            ReadOnly = true, MultiSelect = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false, GridColor = UiTheme.Border,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeight = 37, AllowUserToResizeRows = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
                SelectionBackColor = UiTheme.SurfaceAlt, SelectionForeColor = UiTheme.Text,
                Font = new Font("Segoe UI", 9.3F, FontStyle.Bold)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.Surface, ForeColor = UiTheme.Text,
                SelectionBackColor = Color.FromArgb(38, 83, 77), SelectionForeColor = UiTheme.Text
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text,
                SelectionBackColor = Color.FromArgb(38, 83, 77), SelectionForeColor = UiTheme.Text
            },
            RowTemplate = { Height = 32 }
        };
        void AddColumn(string title, int width)
        {
            _marketGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
        AddColumn("서버", 140);
        AddColumn("레벨", 86);
        AddColumn("추적 기준가", 134);
        AddColumn("경쟁 최고가", 138);
        AddColumn("가격 차이", 120);
        AddColumn("일반 최고가", 140);
        AddColumn("중앙값", 130);
        AddColumn("고가 매물", 98);
        AddColumn("표본", 110);
        DarkScrollbarTheme.Attach(_marketGrid);
        layout.Controls.Add(_marketGrid, 0, 2);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(13, 0, 8, 0),
            Text = "※ 구매글의 검증된 관측값만 표시 · 표본 부족은 별도 표시 · 자동 가격 변경 없음\n" +
                   "   가격을 바꾸려면 통합 편집에서 저장하세요. 사이트 반영은 해당 글의 다음 정상 순번에 시도합니다."
        }, 0, 3);
        view.Controls.Add(layout);
        _workspaceBody.Controls.Add(view);
        _marketView = view;
        view.Visible = false;
    }

    private void RefreshMarketFilters()
    {
        if (_marketServerFilter is null || _marketSnapshot is null) return;
        string selected = _marketServerFilter.SelectedItem?.ToString() ?? "전체";
        var servers = _marketSnapshot.Rows.Select(row => row.Server)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(server => server, StringComparer.CurrentCulture).ToList();
        _marketServerFilter.Items.Clear();
        _marketServerFilter.Items.Add("전체");
        foreach (string server in servers) _marketServerFilter.Items.Add(server);
        _marketServerFilter.SelectedItem = servers.Contains(selected) ? selected : "전체";
    }

    private void RefreshMarketGrid()
    {
        RefreshMarketStatus();
        if (_marketGrid is null || _marketSnapshot is null) return;
        string server = _marketServerFilter?.SelectedItem?.ToString() ?? "전체";
        string level = _marketLevelFilter?.SelectedItem?.ToString() ?? "전체";
        _marketGrid.SuspendLayout();
        try
        {
            _marketGrid.Rows.Clear();
            foreach (MarketBridgeRow row in _marketSnapshot.Rows
                .Where(row => (server == "전체" || row.Server == server) &&
                              (level == "전체" || level == row.Level + "레벨"))
                .OrderBy(row => row.Server, StringComparer.CurrentCulture)
                .ThenBy(row => int.Parse(row.Level)))
            {
                string price(long? value) => value is > 0 ? value.Value.ToString("N0") + "원" : "—";
                string reference = row.Basis.Contains("미확인", StringComparison.Ordinal)
                    ? "미확인" : price(row.MyPrice);
                long? gap = row.MyPrice is > 0 ? row.Highest - row.MyPrice.Value : null;
                string difference = gap.HasValue ? (gap > 0 ? "+" : "") + gap.Value.ToString("N0") + "원" : "—";
                int index = _marketGrid.Rows.Add(row.Server, row.Level + "레벨", reference,
                    price(row.Highest), difference,
                    row.Distinct >= 5 ? price(row.NormalMax) : "표본 부족",
                    price(row.Median), row.HighCount.ToString("N0") + "건",
                    row.Distinct.ToString("N0") + "그룹 / " + row.Total.ToString("N0") + "건");
                if (gap > 0)
                    _marketGrid.Rows[index].Cells[4].Style.ForeColor = UiTheme.Warning;
            }
            _marketGrid.ClearSelection();
        }
        finally { _marketGrid.ResumeLayout(); }
    }

    private void RefreshMarketStatus()
    {
        if (_marketConnection is null || _marketSummary is null) return;
        if (_marketBridge is null)
        {
            _marketConnection.Text = "연동 서버 준비 실패 · " + _marketBridgeError;
            _marketConnection.ForeColor = UiTheme.Danger;
            _marketSummary.Text = "다른 프로그램이 시세 연결 포트를 사용 중이거나 설정 파일에 접근할 수 없습니다.";
            return;
        }
        if (_marketSnapshot is null)
        {
            _marketConnection.Text = "자동 연결 대기 · Firefox Gem Tracker v2.8.9.4 이상 필요";
            _marketConnection.ForeColor = UiTheme.Warning;
            _marketSummary.Text = "연결 코드 입력 불필요 · Firefox 구매 목록에서 추적기 실행 → 다음 정상 시세 검사 후 자동 표시 · 자동등록과 무관";
            return;
        }
        TimeSpan age = DateTime.UtcNow - _marketReceivedUtc;
        bool stale = age.TotalSeconds > 120;
        _marketConnection.Text = stale ? $"마지막 수신 {(int)age.TotalMinutes}분 전 · 갱신 중단" :
            $"연결됨 · 마지막 수신 {_marketReceivedUtc.ToLocalTime():HH:mm:ss}";
        _marketConnection.ForeColor = stale ? UiTheme.Warning : UiTheme.Success;
        _marketSummary.Text =
            $"관측 구매글 {_marketSnapshot.EligiblePosts:N0}건 · 서버/레벨 {_marketSnapshot.Rows.Count:N0}조합" +
            $" · 고가 분리 {_marketSnapshot.Rows.Sum(row => row.HighCount):N0}건" +
            (stale ? " · ⚠ 현재 실시간 시세가 아닙니다. Firefox를 확인하세요." : " · 추적기 주기에 맞춰 관측");
    }
}
