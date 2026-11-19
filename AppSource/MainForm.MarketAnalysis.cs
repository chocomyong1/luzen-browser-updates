using System;
using System.Collections.Generic;
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
    private bool _marketAdviceStaleShown;
    private bool _marketVisible;
    private Panel? _marketView;
    private DataGridView? _marketGrid;
    private Label? _marketSummary;
    private Label? _marketConnection;
    private ComboBox? _marketServerFilter;
    private ComboBox? _marketLevelFilter;
    private Button? _marketButton;
    private bool _marketRefreshingFilters;
    private readonly System.Windows.Forms.Timer _marketStatusTimer = new() { Interval = 10000 };

    private sealed class MarketListingChoice
    {
        public required string System { get; init; }
        public required ListingJob Job { get; init; }
        public required string OriginalPrice { get; init; }
        public override string ToString() =>
            $"[{System}] {Job.Price}원 · {Job.Title} · {Job.ListingNumber}";
    }

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
                        _marketAdviceStaleShown = false;
                        RefreshMarketFilters();
                        RefreshMarketGrid();
                    }));
                }
                catch (InvalidOperationException) { } // Window closing.
            };
            _marketBridge.Start();
            _marketStatusTimer.Tick += (_, _) =>
            {
                RefreshMarketStatus();
                if (_marketVisible && _marketSnapshot is not null && !_marketAdviceStaleShown &&
                    DateTime.UtcNow - _marketReceivedUtc > TimeSpan.FromSeconds(120))
                {
                    _marketAdviceStaleShown = true;
                    RefreshMarketGrid(); // Hide stale recommendations; no timer-based site actions.
                }
            };
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
        _status.Text = "  시세 분석 · 8·9레벨 경쟁가 +100원 / 10레벨 +1,000원 · 실제 적용은 개별 승인 후 다음 순번";
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
        _marketServerFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_marketRefreshingFilters) RefreshMarketGrid();
        };
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
        filters.Controls.Add(IntegratedButton("선택 추천가 검토", 150, ReviewSelectedMarketPrice, primary: true));
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
        AddColumn("서버", 125);
        AddColumn("레벨", 82);
        AddColumn("추적 기준가", 138);
        AddColumn("경쟁 최고가", 150);
        AddColumn("추천가", 145);
        AddColumn("가격 차이", 125);
        AddColumn("추천 판단", 280);
        AddColumn("경쟁 표본", 155);
        _marketGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _marketGrid.Rows[e.RowIndex].Tag is not MarketBridgeRow) return;
            _marketGrid.ClearSelection();
            _marketGrid.Rows[e.RowIndex].Selected = true;
            ReviewSelectedMarketPrice(); // Opens review only; nothing is approved or submitted here.
        };
        DarkScrollbarTheme.Attach(_marketGrid);
        layout.Controls.Add(_marketGrid, 0, 2);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(13, 0, 8, 0),
            Text = "※ 시세표 행 더블클릭 → 추천가 검토 · 등록번호로 확인된 내 글은 경쟁가에서 제외\n" +
                   "   8·9레벨 +100원 / 10레벨 +1,000원 · 개별 승인 후 그 게시글의 다음 정상 순번에만 반영됩니다 (마진 직접 확인)."
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
        _marketRefreshingFilters = true;
        try
        {
            _marketServerFilter.Items.Clear();
            _marketServerFilter.Items.Add("전체");
            foreach (string server in servers) _marketServerFilter.Items.Add(server);
            _marketServerFilter.SelectedItem = servers.Contains(selected) ? selected : "전체";
        }
        finally { _marketRefreshingFilters = false; }
    }

    private void RefreshMarketGrid()
    {
        RefreshMarketStatus();
        if (_marketGrid is null || _marketSnapshot is null) return;
        string server = _marketServerFilter?.SelectedItem?.ToString() ?? "전체";
        string level = _marketLevelFilter?.SelectedItem?.ToString() ?? "전체";
        MarketBridgeRow? selectedRow = _marketGrid.SelectedRows.Count == 1
            ? _marketGrid.SelectedRows[0].Tag as MarketBridgeRow : null;
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
                long? competitor = row.CompetitorHighest > 0 ? row.CompetitorHighest : null;
                long? gap = row.MyPrice is > 0 && competitor.HasValue
                    ? competitor.Value - row.MyPrice.Value : null;
                string difference = gap.HasValue ? (gap > 0 ? "+" : "") + gap.Value.ToString("N0") + "원" : "—";
                MarketAdvice advice = MarketPriceAdvisor.Evaluate(row, _marketReceivedUtc, DateTime.UtcNow);
                string recommendation = price(advice.SuggestedPrice);
                string decision = advice.Kind switch
                {
                    MarketAdviceKind.Increase => "인상 검토 · " + advice.Confidence,
                    MarketAdviceKind.Hold => "기준가보다 낮음 · 수동 판단",
                    _ => advice.Reason
                };
                int index = _marketGrid.Rows.Add(row.Server, row.Level + "레벨", reference,
                    price(competitor), recommendation, difference, decision,
                    row.Distinct.ToString("N0") + "그룹 / " + row.Total.ToString("N0") + "건");
                _marketGrid.Rows[index].Tag = row;
                _marketGrid.Rows[index].Cells[6].ToolTipText = advice.Reason;
                if (advice.Kind == MarketAdviceKind.Increase)
                    _marketGrid.Rows[index].Cells[6].Style.ForeColor = UiTheme.Success;
                if (gap > 0)
                    _marketGrid.Rows[index].Cells[5].Style.ForeColor = UiTheme.Warning;
            }
            _marketGrid.ClearSelection();
            if (selectedRow is not null)
            {
                foreach (DataGridViewRow gridRow in _marketGrid.Rows)
                {
                    if (gridRow.Tag is not MarketBridgeRow current ||
                        current.Server != selectedRow.Server || current.Level != selectedRow.Level) continue;
                    gridRow.Selected = true;
                    break;
                }
            }
        }
        finally { _marketGrid.ResumeLayout(); }
    }

    private void ReviewSelectedMarketPrice()
    {
        if (_marketGrid is null || _marketGrid.SelectedRows.Count != 1 ||
            _marketGrid.SelectedRows[0].Tag is not MarketBridgeRow selectedMarket)
        {
            MessageBox.Show(this, "먼저 시세표에서 서버·레벨 한 줄을 선택하세요.", "시세 추천 선택");
            return;
        }
        MarketAdvice initial = MarketPriceAdvisor.Evaluate(selectedMarket, _marketReceivedUtc, DateTime.UtcNow);
        if (initial.Kind != MarketAdviceKind.Increase || initial.SuggestedPrice is not > 0)
        {
            MessageBox.Show(this, $"현재 인상 추천을 적용할 수 없습니다.\n{initial.Reason}\n" +
                "추적 기준가가 경쟁 추천가보다 높은 경우에는 디스코드 판매 시세를 보고 통합 편집에서 직접 낮출 수 있습니다.",
                "추천 보류", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        long proposedPrice = initial.SuggestedPrice.Value;
        string priceText = proposedPrice.ToString("N0");
        var candidates = new List<MarketListingChoice>();
        foreach (string system in new[] { "A", "B", "C" })
        {
            foreach (ListingJob job in IntegratedOwner(system).IntegratedEditJobs)
            {
                if (!ListingIdentity.HasNumber(job.ListingNumber) ||
                    job.OriginalTradeSide != "구매" ||
                    !MarketPriceAdvisor.MatchesBuyListing(selectedMarket, job.Game,
                        job.OriginalTradeSide, job.Server, job.Title)) continue;
                candidates.Add(new MarketListingChoice
                {
                    System = system, Job = job, OriginalPrice = job.Price
                });
            }
        }
        if (candidates.Count == 0)
        {
            MessageBox.Show(this,
                "이 서버·레벨에 대응하는 본인 구매글을 안전하게 식별하지 못했습니다.\n" +
                "제목에 '8레벨', '9레벨' 또는 '10레벨'이 명확히 들어간 본인 등록글만 추천 적용 대상으로 찾습니다.\n" +
                "기존 글은 통합 편집에서 직접 가격을 변경할 수 있습니다.",
                "일치하는 등록글 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = $"{selectedMarket.Server} · {selectedMarket.Level}레벨 추천가 검토",
            Size = new Size(865, 525), MinimumSize = new Size(865, 525),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, Font = Font
        };
        dialog.Controls.Add(new Label
        {
            Left = 21, Top = 16, Width = 815, Height = 86,
            Text = $"추천가 {priceText}원  |  추적 기준가 {selectedMarket.MyPrice:N0}원  |  {initial.Confidence}\n" +
                   $"경쟁 최고가 {selectedMarket.CompetitorHighest:N0}원 + {(selectedMarket.Level == "10" ? "1,000" : "100")}원 · 디스코드 판매 시세와 마진은 직접 확인하세요.\n" +
                   "적용할 정확한 본인 구매글만 체크하세요. 처음에는 아무것도 선택하지 않습니다.",
            ForeColor = UiTheme.Text, Font = new Font(Font, FontStyle.Regular)
        });
        var choices = new CheckedListBox
        {
            Left = 21, Top = 107, Width = 815, Height = 285, CheckOnClick = true,
            HorizontalScrollbar = true,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
        };
        foreach (MarketListingChoice option in candidates) choices.Items.Add(option, false);
        dialog.Controls.Add(choices);
        dialog.Controls.Add(new Label
        {
            Left = 21, Top = 402, Width = 815, Height = 40,
            Text = "⚠ 높은 경쟁가를 따라가면 마진이 줄 수 있습니다. 디스코드 시세를 확인하고 승인하세요.\n" +
                   "   승인 시 로컬 가격만 저장하고, 각 글의 다음 자동등록 순번에 사이트 가격을 수정합니다.",
            ForeColor = UiTheme.Muted
        });
        var cancel = new Button { Text = "취소", Left = 580, Top = 450, Width = 106, Height = 34 };
        var approve = new Button { Text = "선택한 글 승인", Left = 697, Top = 450, Width = 139, Height = 34 };
        UiTheme.StyleButton(cancel);
        UiTheme.StyleButton(approve, UiTheme.ButtonTone.Primary);
        cancel.Click += (_, _) => dialog.Close();
        approve.Click += (_, _) =>
        {
            var chosen = choices.CheckedItems.Cast<MarketListingChoice>().ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(dialog, "가격을 바꿀 게시글을 먼저 체크하세요.", "선택 없음");
                return;
            }
            MarketBridgeRow? latest = _marketSnapshot?.Rows.FirstOrDefault(row =>
                row.Server == selectedMarket.Server && row.Level == selectedMarket.Level);
            MarketAdvice current = latest is null ? default :
                MarketPriceAdvisor.Evaluate(latest, _marketReceivedUtc, DateTime.UtcNow);
            if (current.Kind != MarketAdviceKind.Increase || current.SuggestedPrice != proposedPrice)
            {
                MessageBox.Show(dialog, "시세가 바뀌었거나 갱신이 끊겼습니다. 최신 추천가를 다시 확인하세요.",
                    "추천가 갱신 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (chosen.Any(choice => choice.Job.Price != choice.OriginalPrice ||
                !string.IsNullOrWhiteSpace(choice.Job.PendingSitePriceTarget) ||
                !long.TryParse((choice.Job.Price ?? "").Replace(",", ""), out long currentPrice) ||
                !MarketPriceAdvisor.SafeBuyPriceIncrease(currentPrice, proposedPrice)))
            {
                MessageBox.Show(dialog,
                    "선택한 글 중 이미 가격 변경이 예약됐거나, 가격이 바뀌었거나, 현재가가 추천가 이상인 글이 있습니다.\n" +
                    "해당 글은 체크 해제하고 다시 검토하세요. 기존 예약을 덮어쓰거나 가격을 내리지 않습니다.",
                    "게시글 가격 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var command = new IntegratedEditCommand(IntegratedEditField.Price, proposedPrice.ToString());
            var groups = chosen.GroupBy(choice => choice.System).OrderBy(group => group.Key)
                .Select(group => (System: group.Key, Form: IntegratedOwner(group.Key),
                    Jobs: (IReadOnlyList<ListingJob>)group.Select(choice => choice.Job).ToList())).ToList();
            foreach (var group in groups)
            {
                if (group.Form.ValidateIntegratedEdit(group.Jobs, command, out string problem)) continue;
                MessageBox.Show(dialog, $"{group.System} 시스템: {problem}\n가격은 변경되지 않았습니다.",
                    "가격 승인 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string preview = string.Join("\n", chosen.Take(15).Select(choice =>
                $"[{choice.System}] {choice.Job.Server} · {choice.Job.ListingNumber} · {choice.Job.Price}원 → {priceText}원"));
            if (chosen.Count > 15) preview += $"\n그 외 {chosen.Count - 15}건";
            if (MessageBox.Show(dialog, $"총 {chosen.Count}개 글의 가격을 승인할까요?\n\n{preview}\n\n" +
                "지금은 사이트에 제출하지 않습니다. 각 글의 다음 정상 등록 차례에만 적용합니다.\n" +
                "자동등록 주기·시간당 제한은 그대로 유지됩니다.",
                "추천 가격 최종 승인", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var receipts = new List<(AutomationForm Form, BatchUndoToken Receipt)>();
            try
            {
                foreach (var group in groups)
                {
                    IntegratedEditResult saved = group.Form.ApplyIntegratedEdit(group.Jobs, command);
                    receipts.Add((group.Form, saved.UndoToken));
                }
            }
            catch (Exception ex)
            {
                _integratedLastUndo = receipts;
                MessageBox.Show(dialog,
                    $"가격 저장에 실패했습니다: {ex.Message}\n" +
                    (receipts.Count > 0 ? "이미 저장된 일부 시스템은 통합 편집에서 마지막 수정 되돌리기를 사용할 수 있습니다." :
                        "변경된 글이 없습니다."),
                    "가격 저장 확인", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _integratedLastUndo = receipts;
            _status.Text = $"  추천가 승인: {chosen.Count}건 · {priceText}원 · 다음 게시글 등록 순번 반영 · 추가 제출 없음";
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        dialog.Controls.Add(cancel);
        dialog.Controls.Add(approve);
        UiTheme.ApplyControlStyles(dialog);
        dialog.ShowDialog(this);
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
            _marketConnection.Text = "자동 연결 대기 · Firefox Gem Tracker v2.8.9.5 이상 필요";
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
            $"관측 구매글 {_marketSnapshot.EligiblePosts:N0}건 · 경쟁 서버/레벨 {_marketSnapshot.Rows.Count:N0}조합 · 확인된 내 글은 경쟁 최고가에서 제외" +
            (stale ? " · ⚠ 현재 실시간 시세가 아닙니다. Firefox를 확인하세요." : " · 추적기 주기에 맞춰 관측");
    }
}
