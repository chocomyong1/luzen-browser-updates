using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // Older imports were all marked RequiresReview. A fresh import has no
    // uncertain POST from this application; it is distinct from a submission
    // that may have succeeded but whose response was not verified.
    private static bool CanConfirmImportedReviewForSingleRun(ListingJob job)
    {
        if (!job.ImportedFromSite || !job.InitialCompleted ||
            job.Operation != "재등록") return false;
        string status = job.LastStatus ?? "";
        return !status.StartsWith("제출 중", StringComparison.Ordinal) &&
               !status.StartsWith("제출 결과 확인 필요", StringComparison.Ordinal) &&
               !status.StartsWith("중지됨 (사이트 확인 필요)", StringComparison.Ordinal) &&
               !status.StartsWith("등록 성공 / 재등록 물품 URL 확인 필요", StringComparison.Ordinal) &&
               !status.StartsWith("실행 중", StringComparison.Ordinal);
    }

    // The user must check the specific existing listing before acknowledging
    // it. The checkbox affects only the selected item; no batch unlock or
    // automatic re-enabling is performed here.
    private bool ConfirmImportedReviewForSingleRun(ListingJob job)
    {
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
            Text = "아이템매니아에서 불러온 기존 글은 자동실행이 보류되어 있습니다.\n" +
                   "해당 등록번호가 현재 내 글이고 실제 금액이 맞는지 사이트에서 확인하세요.\n" +
                   "확인되지 않은 글이나 이미 제출했지만 성공 여부가 불분명한 글은 실행하지 마세요.\n\n" +
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
            Text = "사이트에서 해당 등록번호·금액·현재 등록 상태를 직접 확인했습니다."
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
            Text = "확인 후 1건 실행", Width = 146, Height = 32,
            DialogResult = DialogResult.OK, Enabled = false
        };
        checkedBox.CheckedChanged += (_, _) => proceed.Enabled = checkedBox.Checked;
        var open = new Button { Text = "사이트 물품/목록 열기", Width = 165, Height = 32 };
        open.Click += (_, _) =>
        {
            string url = MyListingsImport.IsSafeDetailUrl(job.EntryUrl, job.ListingNumber)
                ? job.EntryUrl : OwnedListUrlFor(job);
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
