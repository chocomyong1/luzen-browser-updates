using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // One visible Apply button per field, as in the reference application.
    // Every application affects ONLY checked rows in the currently filtered list.
    private void ShowModernBatchEditor() => _ = ShowModernBatchEditorAsync();

    private async Task ShowModernBatchEditorAsync()
    {
        // An ongoing read-only import owns the WebView. Stop that import and
        // await its finally block before letting a user change local prices.
        // Never cancel a live site submission or write into a pending import.
        if (_myListingsImportRunning && !await CancelImportForManualRunAsync("선택 물품 일괄수정")) return;
        if (_sitePriceBatchRunning || _myListingsImportRunning || (_busy && _currentlyExecutingJob is null))
        {
            MessageBox.Show(this, "진행 중인 사이트 제출 또는 목록 조회가 끝난 뒤 일괄수정하세요.",
                "일괄수정 대기", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        // Never save a stale detail editor into the item currently being
        // submitted. The batch editor handles that item after completion.
        if (!_busy) SaveSelectedJob(false);
        var items = _filteredJobs.Where(j => j.SelectedForBulk).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this,
                "1. 물품 목록 맨 위 '☐ 전체'를 누르면 현재 조회된 글을 한 번에 선택합니다.\n2. 원하는 글만 고르려면 왼쪽 체크박스나 우클릭 조건 선택을 사용하세요.\n3. '선택 물품 일괄수정'에서 재등록 주기·연속 재등록 등을 설정하세요.",
                "일괄수정할 물품 선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var window = new Form
        {
            Text = $"일괄수정 | 선택한 {items.Count}개 물품",
            Font = Font, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, MinimumSize = new Size(740, 540),
            ClientSize = new Size(870, 720), BackColor = UiTheme.Surface,
            MinimizeBox = false, MaximizeBox = true, ShowInTaskbar = false
        };
        var header = new Panel { Dock = DockStyle.Top, Height = 89, BackColor = UiTheme.Canvas };
        header.Controls.Add(new Label
        {
            Text = $"선택 물품 {items.Count}개 일괄수정", Dock = DockStyle.Top, Height = 45,
            Padding = new Padding(19, 13, 0, 0), Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            ForeColor = UiTheme.Text
        });
        header.Controls.Add(new Label
        {
            Text = "가격을 적용하면 기존 본인 구매글은 다음 등록 순번에 반영하고 신규글은 첫 등록에 사용합니다.",
            Dock = DockStyle.Bottom, Height = 38, ForeColor = UiTheme.Muted,
            Padding = new Padding(19, 8, 0, 0)
        });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = UiTheme.SurfaceAlt };
        var result = new Label
        {
            Text = "변경 전 상태 · 선택한 물품에만 적용됩니다.", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0),
            ForeColor = UiTheme.Text
        };
        var close = new Button { Text = "닫기", Dock = DockStyle.Right, Width = 107, Margin = new Padding(8) };
        close.Click += (_, _) => window.Close();
        BatchUndoToken? dialogUndo = null;
        var undo = new Button { Text = "마지막 수정 되돌리기", Dock = DockStyle.Right,
            Width = 163, Margin = new Padding(8), Enabled = false };
        undo.Click += (_, _) =>
        {
            if (!CanUndoBatchEdit(dialogUndo, out var issue)) { Error(issue); return; }
            if (MessageBox.Show(window,
                $"마지막 '{dialogUndo!.Label}' 일괄수정을 로컬 설정에서 되돌릴까요?\n사이트 제출은 되돌리지 않습니다.",
                "일괄수정 되돌리기", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!TryUndoBatchEdit(dialogUndo, out issue)) { Error(issue); return; }
            result.Text = "✓ " + issue;
            undo.Enabled = false;
        };
        footer.Controls.Add(result);
        footer.Controls.Add(close);
        footer.Controls.Add(undo);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Surface };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3, Padding = new Padding(16, 12, 18, 15),
            BackColor = UiTheme.Surface, GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        int nextRow = 0;
        void Section(string title)
        {
            var label = new Label { Text = title, Dock = DockStyle.Fill, AutoSize = false,
                Font = new Font(Font, FontStyle.Bold), ForeColor = UiTheme.Primary,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(9, 0, 0, 0),
                BackColor = UiTheme.SurfaceAlt };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.Controls.Add(label, 0, nextRow);
            table.SetColumnSpan(label, 3);
            nextRow++;
        }
        void Row(string labelText, Control input, Action callback, int height = 44)
        {
            int rowId = nextRow++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var label = new Label
            {
                Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), ForeColor = UiTheme.Text
            };
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(5, 5, 8, 5);
            var apply = new Button
            {
                Text = "적용", Dock = DockStyle.Fill, Margin = new Padding(5, 5, 3, 5),
                BackColor = UiTheme.SurfaceAlt, Cursor = Cursors.Hand
            };
            apply.Click += (_, _) => callback();
            table.Controls.Add(label, 0, rowId);
            table.Controls.Add(input, 1, rowId);
            table.Controls.Add(apply, 2, rowId);
        }
        TextBox Input(string hint, bool multiline = false) => new()
        {
            PlaceholderText = hint, Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            BackColor = UiTheme.Surface
        };
        ComboBox Choice(params string[] values)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(values);
            c.SelectedIndex = 0;
            return c;
        }

        bool ApplyField<T>(string field, Func<ListingJob, T> read, Action<ListingJob, T> write,
            T value, bool confirmLocalSave = true, Func<ListingJob, string?>? pendingForJob = null)
        {
            // If an earlier edit changed the current search results (e.g. title),
            // don't silently modify jobs that are now hidden by the active query.
            var active = _filteredJobs.Where(j => j.SelectedForBulk && items.Contains(j)).ToList();
            if (active.Count == 0)
            {
                Error("현재 조회 조건에서 선택된 물품이 없습니다. 창을 닫고 조회 조건과 체크 상태를 확인하세요.");
                return false;
            }
            if (field == "물품 제목" && active.Any(j => ListingIdentity.HasNumber(j.ListingNumber)))
            {
                Error("기존 사이트 등록글의 제목은 여기서 변경할 수 없습니다. " +
                    "사이트의 원본 제목과 달라지면 자동 재등록이 차단됩니다. " +
                    "등록번호가 없는 신규 물품만 선택해 주세요.");
                return false;
            }
            if (confirmLocalSave && MessageBox.Show(window,
                $"체크한 {active.Count}건의 '{field}'만 바꿀까요?\n\n이 작업은 프로그램의 물품 설정을 변경하며 사이트에 등록된 원본 글은 즉시 수정하지 않습니다.",
                "일괄수정 적용", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
            var previous = active.Select(item => (Item: item, OldValue: read(item))).ToList();
            var oldReservations = active.Select(item => (Item: item, item.PendingSitePriceTarget)).ToList();
            var immediate = previous.Where(x => !ReferenceEquals(x.Item, _currentlyExecutingJob)).ToList();
            var deferred = previous.Where(x => ReferenceEquals(x.Item, _currentlyExecutingJob)).ToList();
            var changes = active.Select(item => CaptureBatchUndoChange(item, read, write, value,
                pendingForJob?.Invoke(item))).ToList();
            var byJob = changes.ToDictionary(change => change.Job);
            int deferredStart = _deferredExecutingJobEdits.Count;
            foreach (var (item, _) in immediate) ApplyBatchUndoChange(byJob[item]);
            foreach (var (item, _) in deferred)
            {
                var change = byJob[item];
                Action afterSubmission = () => ApplyBatchUndoChange(change);
                change.DeferredAction = afterSubmission;
                _deferredExecutingJobEdits.Add(afterSubmission);
            }
            try
            {
                _config.Jobs = _jobs.ToList();
                _config.Save();
            }
            catch (Exception ex)
            {
                foreach (var (item, oldValue) in immediate) write(item, oldValue);
                foreach (var (item, pending) in oldReservations)
                    item.PendingSitePriceTarget = pending;
                _deferredExecutingJobEdits.RemoveRange(deferredStart,
                    _deferredExecutingJobEdits.Count - deferredStart);
                _config.Jobs = _jobs.ToList();
                MessageBox.Show(window, "설정 저장에 실패했습니다. 이 항목의 변경은 되돌렸습니다.\n" + ex.Message,
                    "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            // Reset selection to prevent stale editor controls from overwriting the
            // newly updated item when the focus is moved to another row.
            _selected = null;
            RefreshFilteredLists();
            result.Text = deferred.Count > 0
                ? $"✓ {field}: {immediate.Count}건 저장 · 진행 중인 {deferred.Count}건은 작업 종료 후 적용"
                : $"✓ {field}: 선택한 {active.Count}건에 적용 완료";
            AppendLog($"일괄수정 {field}: {immediate.Count}건 즉시 저장" +
                (deferred.Count > 0 ? $" · {deferred.Count}건 작업 종료 후 적용 예약" : ""));
            dialogUndo = RememberBatchUndo(field, changes);
            undo.Enabled = true;
            return true;
        }
        void Error(string message) => MessageBox.Show(window, message, "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        Section("가격 및 거래 수량");
        var priceBox = Input("예: 11,300");
        Row("개당 가격 (원)", priceBox, () =>
        {
            string s = priceBox.Text.Trim().Replace(",", "").Replace("원", "");
            if (!long.TryParse(s, out long price) || price < 1000 || price % 10 != 0)
            { Error("사이트 구매글 가격은 1,000원 이상, 10원 단위로 입력하세요."); return; }
            var activePriceTargets = _filteredJobs.Where(j => j.SelectedForBulk && items.Contains(j)).ToList();
            if (activePriceTargets.Count == 0) { Error("선택한 물품이 없습니다."); return; }
            if (!TryPlanAutomaticPriceApply(activePriceTargets, price.ToString(),
                out var pendingByJob, out var problem))
            { Error(problem); return; }
            var preview = string.Join("\n", activePriceTargets.Take(8).Select(j =>
                $"{j.Server} · {j.ListingNumber} : {j.Price}원 → {price:N0}원"));
            if (activePriceTargets.Count > 8) preview += $"\n외 {activePriceTargets.Count - 8}건";
            if (MessageBox.Show(window,
                $"선택 {activePriceTargets.Count}건을 {price:N0}원으로 변경합니다.\n\n{preview}\n\n" +
                "기존 본인 구매글은 가격만 저장되며 사이트에는 아직 반영되지 않습니다.\n" +
                "신규 판매·구매글은 첫 등록 가격으로 저장합니다.\n" +
                "실제 사이트 수정은 [선택 가격 일괄 반영]을 별도로 눌러 한 번 승인하세요. " +
                "등록번호로 확인하며 결과가 불분명하면 뒤의 글은 제출하지 않습니다.",
                "가격 일괄수정", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (ApplyField("가격", x => x.Price, (x, v) => x.Price = v,
                price.ToString(), confirmLocalSave: false,
                pendingForJob: job => pendingByJob[job]))
            {
                int existingCount = activePriceTargets.Count(job => !IsUnsubmittedNewListing(job));
                result.Text = $"✓ {activePriceTargets.Count}건 저장 · 기존 구매글 {existingCount}건은 사이트 반영 대기";
                AppendLog($"가격 저장: {activePriceTargets.Count}건 · 기존 구매글 사이트 제출 없음, 별도 일괄 반영 필요");
            }
        });
        var quantityBox = Input("예: 3");
        Row("수량", quantityBox, () =>
        {
            if (!int.TryParse(quantityBox.Text.Trim(), out int quantity) || quantity < 1)
            { Error("수량은 1 이상의 정수로 입력하세요."); return; }
            ApplyField("수량", x => x.Quantity, (x, v) => x.Quantity = v, quantity.ToString());
        });
        var minBox = Input("분할거래 최소 수량");
        Row("최소 수량", minBox, () =>
        {
            if (!int.TryParse(minBox.Text.Trim(), out int min) || min < 1) { Error("최소 수량은 1 이상이어야 합니다."); return; }
            if (items.Any(x => x.TradingMethod != "분할")) { Error("일반 거래가 섞여 있습니다. 분할거래만 조회·선택한 후 수정하세요."); return; }
            if (items.Any(x => int.TryParse(x.MaxQuantity, out int max) && min > max))
            { Error("선택한 물품 중 최대 수량이 더 작은 항목이 있습니다. 먼저 최대 수량을 조정하세요."); return; }
            ApplyField("최소 수량", x => x.MinQuantity, (x, v) => x.MinQuantity = v, min.ToString());
        });
        var maxBox = Input("분할거래 최대 수량");
        Row("최대 수량", maxBox, () =>
        {
            if (!int.TryParse(maxBox.Text.Trim(), out int max) || max < 1) { Error("최대 수량은 1 이상이어야 합니다."); return; }
            if (items.Any(x => x.TradingMethod != "분할")) { Error("일반 거래가 섞여 있습니다. 분할거래만 조회·선택한 후 수정하세요."); return; }
            if (items.Any(x => int.TryParse(x.MinQuantity, out int min) && max < min))
            { Error("선택한 물품 중 최소 수량이 더 큰 항목이 있습니다. 먼저 최소 수량을 조정하세요."); return; }
            ApplyField("최대 수량", x => x.MaxQuantity, (x, v) => x.MaxQuantity = v, max.ToString());
        });
        var autoDecrease = Choice("변경 없음", "자동감소 ON", "자동감소 OFF");
        Row("수량 자동감소", autoDecrease, () =>
        {
            if (autoDecrease.SelectedIndex == 0) { Error("자동감소 ON 또는 OFF를 선택하세요."); return; }
            if (items.Any(x => x.TradingMethod != "분할")) { Error("수량 자동감소는 분할거래 물품만 선택해 수정하세요."); return; }
            ApplyField("수량 자동감소", x => x.AutoDecrease, (x, v) => x.AutoDecrease = v,
                autoDecrease.SelectedIndex == 1);
        });

        Section("물품 제목 · 상세내용");
        var titleBox = Input("선택한 물품들의 제목을 같은 값으로 변경");
        Row("물품 제목", titleBox, () =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text)) { Error("제목을 입력하세요. 빈 제목으로 일괄 변경하지 않습니다."); return; }
            ApplyField("물품 제목", x => x.Title, (x, v) => x.Title = v, titleBox.Text.Trim());
        });
        var descBox = Input("적용하면 선택한 물품들의 설명을 교체합니다", true);
        Row("상세 설명", descBox, () =>
        {
            if (string.IsNullOrWhiteSpace(descBox.Text)) { Error("설명을 입력하세요. 빈 내용으로 일괄 변경하지 않습니다."); return; }
            ApplyField("상세 설명", x => x.Description, (x, v) => x.Description = v, descBox.Text);
        }, 102);
        var characterBox = Input("예: 캐릭터명");
        Row("캐릭터명", characterBox, () =>
        {
            if (string.IsNullOrWhiteSpace(characterBox.Text)) { Error("캐릭터명을 입력하세요."); return; }
            ApplyField("캐릭터명", x => x.Character, (x, v) => x.Character = v, characterBox.Text.Trim());
        });

        Section("자동등록 일정 (사이트에 게시하는 동작 아님)");
        var intervalBox = Input("예: 30 (분)");
        Row("재등록 주기 (분)", intervalBox, () =>
        {
            if (!int.TryParse(intervalBox.Text.Trim(), out int minutes) || minutes < 1 || minutes > 10080)
            { Error("재등록 주기는 1~10080분 사이여야 합니다."); return; }
            ApplyField("재등록 주기", x => x.IntervalMinutes, (x, v) => x.IntervalMinutes = v, minutes);
        });
        var autoRepost = Choice("변경 없음", "연속 재등록 ON", "연속 재등록 OFF");
        Row("연속 재등록", autoRepost, () =>
        {
            if (autoRepost.SelectedIndex == 0) { Error("ON 또는 OFF를 선택하세요."); return; }
            if (autoRepost.SelectedIndex == 1 && items.Any(x => x.InitialCompleted && string.IsNullOrWhiteSpace(x.EntryUrl)))
            { Error("등록이 완료되었지만 관리 URL이 없는 물품이 있습니다. 잘못된 글을 재등록하지 않도록 설정을 보류합니다."); return; }
            ApplyField("연속 재등록", x => x.AutoRepost, (x, v) => x.AutoRepost = v, autoRepost.SelectedIndex == 1);
        });
        var autoEnabled = Choice("변경 없음", "자동실행 ON", "자동실행 OFF");
        Row("자동 실행 포함", autoEnabled, () =>
        {
            if (autoEnabled.SelectedIndex == 0) { Error("ON 또는 OFF를 선택하세요."); return; }
            if (autoEnabled.SelectedIndex == 1 && items.Any(x => x.RequiresReview))
            { Error("사이트 확인이 필요한 물품이 포함되어 있어 자동실행을 켤 수 없습니다. 먼저 각 물품을 확인하세요."); return; }
            ApplyField("자동 실행", x => x.Enabled, (x, v) => x.Enabled = v, autoEnabled.SelectedIndex == 1);
        });

        scroll.Controls.Add(table);
        window.Controls.Add(scroll);
        window.Controls.Add(footer);
        window.Controls.Add(header);
        UiTheme.ApplyControlStyles(window);
        window.ShowDialog(this);
    }
}
