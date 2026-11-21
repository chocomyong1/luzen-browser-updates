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
                ? "실제 확인된 사이트 가격과 새 예약 가격을 구분합니다. 다음 자동등록 차례에 반영됩니다."
                : "현재 사이트 가격 자동 수정은 중지되어 있습니다. 표시된 금액은 미적용 요청이며 사이트 가격이 아닙니다.",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(explanation, 0, 1);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, ReadOnly = true, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
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
        var cancelRequest = new Button { Text = "선택 요청 취소", Width = 136, Height = 34 };
        UiTheme.StyleButton(cancelRequest);
        var refresh = new Button { Text = "새로고침", Width = 108, Height = 34 };
        UiTheme.StyleButton(refresh);
        bottom.Controls.Add(close);
        bottom.Controls.Add(cancelRequest);
        bottom.Controls.Add(edit);
        bottom.Controls.Add(refresh);
        bottom.Controls.Add(new Label
        {
            Text = "※ 취소해도 실제 사이트 가격과 저장된 물품 금액은 바뀌지 않습니다.",
            AutoSize = true, Padding = new Padding(0, 10, 14, 0), ForeColor = UiTheme.Muted
        });
        layout.Controls.Add(bottom, 0, 4);

        var rows = new List<PriceReservationRow>();
        PriceReservationRow? Selected() => grid.CurrentRow?.DataBoundItem as PriceReservationRow;
        void ShowSelected()
        {
            var selected = Selected();
            edit.Enabled = selected is not null && SitePriceEdit.AutomaticPriceSubmissionEnabled;
            cancelRequest.Enabled = selected is not null;
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
            string? selectedId = Selected()?.Job.Id;
            string? selectedSystem = Selected()?.System;
            rows = (new[] { "A", "B", "C" })
                .SelectMany(system => IntegratedOwner(system).CurrentPriceReservations()
                    .Select(snapshot => new PriceReservationRow { System = system, Snapshot = snapshot }))
                .OrderBy(row => row.System).ThenBy(row => row.Job.Server)
                .ThenBy(row => row.Job.Title).ToList();
            grid.DataSource = null;
            grid.DataSource = rows;
            if (selectedId is not null)
                foreach (DataGridViewRow gridRow in grid.Rows)
                    if (gridRow.DataBoundItem is PriceReservationRow candidate &&
                        candidate.Job.Id == selectedId && candidate.System == selectedSystem)
                    {
                        grid.CurrentCell = gridRow.Cells[0]; break;
                    }
            header.Text = (SitePriceEdit.AutomaticPriceSubmissionEnabled ? "가격 변경 예약" : "미적용 가격 요청") +
                $"  ·  {rows.Count}건";
            ShowSelected();
        }
        refresh.Click += (_, _) => Reload();
        cancelRequest.Click += (_, _) =>
        {
            var selected = Selected();
            if (selected is null) return;
            if (MessageBox.Show(dialog,
                    selected.SystemName + " · " + selected.Item + "\n\n" +
                    "미적용 가격 요청 " + selected.TargetPrice + "을(를) 취소할까요?\n" +
                    "사이트에 제출하지 않으며, 실제 사이트 가격과 저장된 물품 가격은 바꾸지 않습니다.\n" +
                    "이전 제출이 불확실한 물품은 취소할 수 없습니다.",
                    "가격 요청 1건 취소 · 사이트 미제출", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (!IntegratedOwner(selected.System).TryCancelUnsubmittedPriceReservation(selected.Job, out var problem))
            {
                MessageBox.Show(dialog, problem, "가격 요청 취소 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Reload();
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
        clock.Tick += (_, _) => { if (!dialog.IsDisposed && !grid.IsCurrentCellInEditMode) Reload(); };
        Reload();
        clock.Start();
        dialog.ShowDialog(this);
        clock.Stop();
    }
}
