using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // Only a user's explicit acknowledgement can lift an imported-listing hold.
    // An uncertain prior POST is NEVER eligible for batch acknowledgement.
    // The actual submit path still verifies title + unique registration number.
    private int ApproveImportedReviewsForBulk(IReadOnlyCollection<ListingJob> shown)
    {
        var pending = shown.Where(j => j.RequiresReview).ToList();
        var eligible = pending.Where(j =>
            CanConfirmImportedReviewForSingleRun(j) &&
            ListingIdentity.HasNumber(j.ListingNumber) &&
            MyListingsImport.IsSafeDetailUrl(j.EntryUrl, j.ListingNumber) &&
            GameServerCatalog.IsSupported(j.Game, j.Server) &&
            !string.IsNullOrWhiteSpace(j.Title)).ToList();
        if (eligible.Count == 0) return 0;

        int oddPrice = eligible.Count(j => string.IsNullOrWhiteSpace(j.Price) ||
            j.Price.Trim() is "1" or "0");
        string message = $"조회한 물품 중 내 기존 등록글 {eligible.Count}건을 일괄 확인하고 자동 ON을 준비합니다.\n" +
            $"보류 유지 (등록번호/URL/서버가 불분명하거나 이전 제출 결과 미확인): {pending.Count - eligible.Count}건\n\n" +
            "아래 내용을 사이트에서 직접 확인했을 때만 [예]를 누르세요.\n" +
            "• 선택된 모든 등록번호가 현재 내 물품입니다.\n" +
            "• 사이트에 보이는 물품 종류와 실제 가격을 확인했습니다.\n" +
            "• 자동 재등록 시 이 물품들이 순차적으로 다시 게시될 수 있다는 점을 이해합니다.\n\n" +
            (oddPrice > 0 ? $"주의: {oddPrice}건은 표시 가격이 1원/0원 또는 공란입니다. 실제 사이트 가격을 확인하세요.\n\n" : "") +
            "실제 제출은 별도 [실제 제출 허용]과 [자동 시작] 확인을 거치며,\n" +
            "각 물품의 등록번호·제목 검사는 제출 직전에 다시 수행합니다.";
        if (MessageBox.Show(this, message, "내 등록글 일괄 확인 후 자동 ON", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 0;

        foreach (var job in eligible)
        {
            job.RequiresReview = false;
            job.LastStatus = "기존 글 일괄 확인 · 자동 실행 준비";
            if (job.NextDueUtc == DateTime.MaxValue) job.NextDueUtc = DateTime.UtcNow;
        }
        AppendLog($"사용자가 내 등록글 {eligible.Count}건을 일괄 확인함 · 사이트 제출 전 본문/등록번호 재검증 유지");
        return eligible.Count;
    }
}
