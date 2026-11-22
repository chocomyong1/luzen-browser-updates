using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private sealed class PriceReservationRow
    {
        public required string System { get; init; }
        public required PriceReservationSnapshot Snapshot { get; init; }
        public ListingJob Job => Snapshot.Job;
        public string SystemName => System == "A" ? "A 기본" : System + " VPN";
        public string Item => Job.Server + " · " + Job.Title;
        public string VerifiedPrice => DisplaySitePrice(Job.VerifiedSitePrice);
        public string TargetPrice => DisplaySitePrice(Job.PendingSitePriceTarget);
        public string Estimated => DescribeReservationEta(Snapshot);
        public string State => Snapshot.Status;
    }

    private static string DisplaySitePrice(string price) =>
        long.TryParse((price ?? "").Replace(",", ""), out long amount) && amount > 0
            ? amount.ToString("N0") + "원" : "미확인";

    private static string DescribeReservationEta(PriceReservationSnapshot snapshot)
    {
        if (snapshot.EstimatedUtc is not DateTime due) return "시간 미정";
        TimeSpan remaining = due - DateTime.UtcNow;
        if (remaining <= TimeSpan.FromSeconds(30)) return "곧 (예상)";
        if (remaining < TimeSpan.FromMinutes(90))
            return "약 " + Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) + "분 후";
        return "약 " + Math.Ceiling(remaining.TotalHours * 2) / 2.0 + "시간 후";
    }

    private void ShowPriceReservationsDialog()
    {
        using var dialog = new Form
        {
            Text = "VANTA · 가격 수정 예약 현황", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1090, 690), MinimumSize = new Size(850, 520),
            BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text, Font = Font
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5,
            BackColor = UiTheme.Canvas
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        dialog.Controls.Add(layout);

        var header = new Label
        {
            Text = "가격 변경 요청 현황", Dock = DockStyle.Fill, AutoSize = false,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold), ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(header, 0, 0);
        var explanation = new Label
        {
            Text = SitePriceEdit.AutomaticPriceSubmissionEnabled
                ? "승인한 예약만 다음 자동등록 차례에 반영됩니다."
                : "현재 사이트 가격 자동 수정은 중지되어 있습니다. 표시된 금액은 미적용 요청이며 사이트 가격이 아닙니다.",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(explanation, 0, 1);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, ReadOnly = true, RowHeadersVisible = false,
            MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiTheme.Border, EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 38, RowTemplate = { Height = 37 },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
                SelectionBackColor = UiTheme.SurfaceAlt, SelectionForeColor = UiTheme.Text,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.Surface, ForeColor = UiTheme.Text,
                SelectionBackColor = Color.FromArgb(41, 93, 82), SelectionForeColor = UiTheme.Text,
                Font = new Font("Segoe UI", 9.3F)
            }
        };
        void Column(string property, string heading, int width, bool fill = false) =>
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = property, HeaderText = heading, Width = width,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                MinimumWidth = fill ? 190 : width
            });
        Column(nameof(PriceReservationRow.SystemName), "시스템", 78);
        Column(nameof(PriceReservationRow.Item), "서버 · 물품", 340, fill: true);
        Column(nameof(PriceReservationRow.VerifiedPrice), "현재 사이트 가격", 145);
        Column(nameof(PriceReservationRow.TargetPrice), "미적용 요청 가격", 142);
        Column(nameof(PriceReservationRow.Estimated), "예상 반영", 120);
        Column(nameof(PriceReservationRow.State), "상태", 160);
        layout.Controls.Add(grid, 0, 2);

        var selectedCard = new Label
        {
            Dock = DockStyle.Fill, ForeColor = UiTheme.Text, BackColor = UiTheme.Surface,
            Font = new Font("Segoe UI", 10F), Padding = new Padding(15),
            Text = "예약된 물품을 선택하면 가격과 예상 반영 시간을 확인할 수 있습니다."
        };
        layout.Controls.Add(selectedCard, 0, 3);
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            BackColor = UiTheme.Canvas, Padding = new Padding(0, 4, 0, 0)
        };
        var close = new Button { Text = "닫기", Width = 86, Height = 34 };
        UiTheme.StyleButton(close);
        close.Click += (_, _) => dialog.Close();
        var edit = new Button { Text = "예약 가격 다시 수정", Width = 165, Height = 34 };
        UiTheme.StyleButton(edit, UiTheme.ButtonTone.Primary);
        var reserve = new Button { Text = "다음 순번에 예약", Width = 144, Height = 34 };
        UiTheme.StyleButton(reserve);
        var cancelRequest = new Button { Text = "선택 요청 정리", Width = 158, Height = 34 };
        UiTheme.StyleButton(cancelRequest);
        var refresh = new Button { Text = "새로고침", Width = 108, Height = 34 };
        UiTheme.StyleButton(refresh);
        bottom.Controls.Add(close);
        bottom.Controls.Add(cancelRequest);
        bottom.Controls.Add(reserve);
        bottom.Controls.Add(edit);
        bottom.Controls.Add(refresh);
        layout.Controls.Add(bottom, 0, 4);

        var rows = new List<PriceReservationRow>();
        bool reconciling = false;
        dialog.FormClosing += (_, e) => { if (reconciling) e.Cancel = true; };
        PriceReservationRow? Selected() => grid.CurrentRow?.DataBoundItem as PriceReservationRow;
        List<PriceReservationRow> SelectedRequests() => grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem).OfType<PriceReservationRow>().ToList();
        bool CanClear(PriceReservationRow row)
        {
            var owner = IntegratedOwner(row.System);
            return owner.CanCancelUnsubmittedPriceReservation(row.Job, out _) ||
                owner.CanReconcilePriceReservation(row.Job) || owner.CanDismissPriceRequest(row.Job);
        }
        void ShowSelected()
        {
            if (reconciling) return;
            var selected = Selected();
            var selection = SelectedRequests();
            edit.Enabled = selection.Count == 1 && SitePriceEdit.AutomaticPriceSubmissionEnabled;
            reserve.Enabled = selection.Count > 0 && selection.All(row => !row.Job.RequiresReview) &&
                SitePriceEdit.AutomaticPriceSubmissionEnabled;
            var canCancel = selection.Count(CanClear);
            cancelRequest.Enabled = canCancel > 0;
            if (selection.Count > 1)
            {
                selectedCard.Text = $"선택 {selection.Count}건 · 정리 가능 {canCancel}건\n" +
                    "Ctrl 또는 Shift로 여러 요청을 선택할 수 있습니다.";
                return;
            }
            if (selected is null)
            {
                selectedCard.Text = "대기 중인 가격 변경 예약이 없습니다.";
                return;
            }
            string verified = selected.VerifiedPrice;
            string verifiedAt = selected.Job.VerifiedSitePriceUtc?.ToLocalTime().ToString("MM-dd HH:mm") ?? "확인 기록 없음";
            selectedCard.Text = $"{selected.SystemName}  ·  {selected.Item}\n" +
                $"현재 사이트 가격   {verified}  ({verifiedAt} 확인)\n" +
                $"미적용 요청 가격   {selected.TargetPrice}     예상 반영   {selected.Estimated} ({selected.State})";
        }
        grid.SelectionChanged += (_, _) => ShowSelected();
        void Reload()
        {
            var selectedKeys = SelectedRequests().Select(row => (row.System, row.Job.Id)).ToHashSet();
            rows = (new[] { "A", "B", "C" })
                .SelectMany(system => IntegratedOwner(system).CurrentPriceReservations()
                    .Select(snapshot => new PriceReservationRow { System = system, Snapshot = snapshot }))
                .OrderBy(row => row.System).ThenBy(row => row.Job.Server)
                .ThenBy(row => row.Job.Title).ToList();
            grid.DataSource = null;
            grid.DataSource = rows;
            if (selectedKeys.Count > 0)
            {
                grid.ClearSelection();
                foreach (DataGridViewRow gridRow in grid.Rows)
                    if (gridRow.DataBoundItem is PriceReservationRow candidate &&
                        selectedKeys.Contains((candidate.System, candidate.Job.Id)))
                        gridRow.Selected = true;
            }
            header.Text = (SitePriceEdit.AutomaticPriceSubmissionEnabled ? "가격 변경 예약" : "미적용 가격 요청") +
                $"  ·  {rows.Count}건";
            ShowSelected();
        }
        refresh.Click += (_, _) => Reload();
        reserve.Click += async (_, _) =>
        {
            foreach (var group in SelectedRequests().GroupBy(row => row.System))
                await IntegratedOwner(group.Key).ReserveSitePricesAsync(group.Select(row => row.Job).ToList());
            Reload();
        };
        cancelRequest.Click += async (_, _) =>
        {
            var selection = SelectedRequests();
            var cancellable = selection.Where(CanClear).ToList();
            if (cancellable.Count == 0) return;
            if (MessageBox.Show(dialog,
                    $"선택한 가격 요청 {cancellable.Count}건을 정리할까요?\n\n" +
                    "미제출 요청은 취소하고, 확인 대기는 사이트에서 같은 등록번호의 가격을 조회합니다.\n" +
                    "확인되지 않은 요청은 이 목록에서 빼고 확인 기록을 보관합니다. 사이트에는 가격을 제출하지 않습니다.",
                    "선택 가격 요청 정리", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var failures = new List<string>();
            int dismissed = 0;
            reconciling = true;
            close.Enabled = refresh.Enabled = edit.Enabled = cancelRequest.Enabled = reserve.Enabled = grid.Enabled = false;
            try
            {
                for (var i = 0; i < cancellable.Count; i++)
                {
                    var selected = cancellable[i];
                    selectedCard.Text = $"요청 정리 중 {i + 1}/{cancellable.Count} · {selected.SystemName} · {selected.Job.ListingNumber}";
                    var owner = IntegratedOwner(selected.System);
                    string? problem;
                    if (owner.CanReconcilePriceReservation(selected.Job))
                        problem = await owner.ReconcilePriceReservationAsync(selected.Job);
                    else if (owner.TryCancelUnsubmittedPriceReservation(selected.Job, out var cancelProblem))
                        problem = null;
                    else problem = cancelProblem;
                    if (problem is not null && owner.CanDismissPriceRequest(selected.Job))
                    {
                        problem = owner.DismissPriceRequest(selected.Job);
                        if (problem is null) dismissed++;
                    }
                    if (problem is not null)
                        failures.Add($"{selected.SystemName} · {selected.Job.ListingNumber} · {problem}");
                }
            }
            finally
            {
                reconciling = false;
                close.Enabled = refresh.Enabled = grid.Enabled = true;
                Reload();
            }
            MessageBox.Show(dialog, $"정리 {cancellable.Count - failures.Count}건 · 유지 {selection.Count - cancellable.Count + failures.Count}건" +
                (dismissed == 0 ? "" : $"\n확인 기록을 보관하고 목록에서 뺀 요청 {dismissed}건") +
                (failures.Count == 0 ? "" : "\n\n" + string.Join("\n", failures)),
                "가격 요청 정리 결과", MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };
        edit.Click += (_, _) =>
        {
            if (!SitePriceEdit.AutomaticPriceSubmissionEnabled) return;
            var selected = Selected();
            if (selected is null) return;
            using var prompt = new Form
            {
                Text = "예약 가격 교체", Size = new Size(420, 235), MinimumSize = new Size(420, 235),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Text, Font = Font
            };
            var label = new Label
            {
                Text = $"{selected.Item}\n현재 예약: {selected.TargetPrice}  (다음 정상 순번 적용)",
                Left = 20, Top = 18, Width = 370, Height = 55, ForeColor = UiTheme.Text
            };
            var input = new TextBox
            {
                Left = 20, Top = 86, Width = 365, Height = 32, Text = selected.Job.PendingSitePriceTarget,
                BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text
            };
            var save = new Button { Text = "예약 교체", Left = 270, Top = 137, Width = 116, Height = 36 };
            UiTheme.StyleButton(save, UiTheme.ButtonTone.Primary);
            var cancel = new Button { Text = "취소", Left = 171, Top = 137, Width = 86, Height = 36 };
            UiTheme.StyleButton(cancel);
            cancel.Click += (_, _) => prompt.Close();
            save.Click += (_, _) =>
            {
                string value = input.Text.Trim().Replace(",", "").Replace("원", "");
                var command = new IntegratedEditCommand(IntegratedEditField.Price, value);
                var owner = IntegratedOwner(selected.System);
                if (!owner.ValidateIntegratedEdit(new[] { selected.Job }, command, out string problem))
                {
                    MessageBox.Show(prompt, problem, "예약 교체 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    owner.ApplyIntegratedEdit(new[] { selected.Job }, command);
                    prompt.DialogResult = DialogResult.OK;
                    prompt.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(prompt, ex.Message, "예약 교체 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            prompt.Controls.AddRange(new Control[] { label, input, save, cancel });
            prompt.AcceptButton = save;
            prompt.CancelButton = cancel;
            if (prompt.ShowDialog(dialog) == DialogResult.OK) Reload();
        };
        using var clock = new System.Windows.Forms.Timer { Interval = 5000 };
        clock.Tick += (_, _) => { if (!reconciling && !dialog.IsDisposed && !grid.IsCurrentCellInEditMode) Reload(); };
        Reload();
        clock.Start();
        dialog.ShowDialog(this);
        clock.Stop();
    }
}
