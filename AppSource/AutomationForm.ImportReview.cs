using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // A selected imported item can be manually acknowledged for ONE run after
    // inspecting its actual site state. Keep past uncertain submissions held
    // until that explicit acknowledgement; never auto-enable the scheduler.
    private static bool CanConfirmImportedReviewForSingleRun(ListingJob job)
    {
        return job.ImportedFromSite && job.InitialCompleted &&
               job.Operation == "재등록" &&
               ListingIdentity.HasNumber(job.ListingNumber) &&
               MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job));
    }

    private static bool HasUnverifiedSubmission(ListingJob job)
    {
        string status = job.LastStatus ?? "";
        return job.PendingRepostAttemptUtc.HasValue ||
               status.StartsWith("제출 중", StringComparison.Ordinal) ||
               status.StartsWith("제출 결과 확인 필요", StringComparison.Ordinal) ||
               status.StartsWith("중지됨 (사이트 확인 필요)", StringComparison.Ordinal) ||
               status.StartsWith("작업 오류", StringComparison.Ordinal) ||
               status.StartsWith("실행 중", StringComparison.Ordinal);
    }

    // The user must check the specific existing listing before acknowledging
    // it. The checkbox affects only the selected item; no batch unlock or
    // automatic re-enabling is performed here.
    private bool ConfirmImportedReviewForSingleRun(ListingJob job)
    {
        bool hasUnverifiedSubmission = HasUnverifiedSubmission(job);
        using var dialog = new Form
        {
            Text = "불러온 등록글 확인 · 1건만 실행",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(710, 390),
            MinimumSize = new Size(550, 345),
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = UiTheme.Surface,
            Font = this.Font
        };
        var summary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(15, 12, 15, 5),
            Text = (hasUnverifiedSubmission
                    ? "주의: 이 글은 이전 재등록 시도가 이미 성공했을 수 있습니다.\n" +
                      "사이트에서 이전 시도의 등록시각과 중복 여부를 먼저 확인하세요.\n"
                    : "사이트에서 불러온 기존 글입니다. 현재 등록 상태를 확인해 주세요.\n") +
                   "확인되지 않은 글은 실행하지 마세요. 확인하더라도 자동 실행은 켜지지 않습니다.\n\n" +
                   $"등록번호: {job.ListingNumber}\n" +
                   $"거래: {job.OriginalTradeSide}   게임/서버: {job.Game} / {job.Server}\n" +
                   $"표시 가격: {job.Price}원 (불러오기 오류 가능, 특히 1원이라면 주의)\n" +
                   $"제목: {job.Title}"
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 112, Padding = new Padding(12, 5, 12, 8) };
        var checkedBox = new CheckBox
        {
            Dock = DockStyle.Top,
            Height = 38,
            AutoSize = false,
            Text = hasUnverifiedSubmission
                ? "이전 재등록 결과·중복 여부와 해당 등록번호·금액을 사이트에서 확인했습니다."
                : "사이트에서 해당 등록번호·금액·현재 등록 상태를 직접 확인했습니다."
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 49,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var cancel = new Button { Text = "취소", Width = 105, Height = 32, DialogResult = DialogResult.Cancel };
        var proceed = new Button
        {
            Text = hasUnverifiedSubmission ? "확인 후 1건 재시도" : "확인 후 1건 실행", Width = 157, Height = 32,
            DialogResult = DialogResult.OK, Enabled = false
        };
        checkedBox.CheckedChanged += (_, _) => proceed.Enabled = checkedBox.Checked;
        var open = new Button { Text = "사이트 물품/목록 열기", Width = 165, Height = 32 };
        open.Click += (_, _) =>
        {
            string url = OwnedListUrlFor(job);
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(dialog, "이 글의 상세 URL 또는 내 등록글 목록을 확인할 수 없습니다.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog, "브라우저를 열지 못했습니다.\n" + ex.Message);
            }
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(proceed);
        buttons.Controls.Add(open);
        bottom.Controls.Add(checkedBox);
        bottom.Controls.Add(buttons);
        dialog.Controls.Add(summary);
        dialog.Controls.Add(bottom);
        dialog.AcceptButton = proceed;
        dialog.CancelButton = cancel;
        UiTheme.ApplyControlStyles(dialog);
        UiTheme.StyleButton(proceed, UiTheme.ButtonTone.Primary);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
