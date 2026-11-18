using System;
using System.Linq;

namespace LuzenBrowserReplica;

// A compact presentation layer. The raw diagnostic log is retained separately
// for error copying and local log files; filtering never removes evidence.
internal readonly record struct LogPresentationResult(bool Visible, bool IsError, string Text);

internal static class LogPresentation
{
    private static readonly string[] AdministrativeMessages =
    {
        "스케줄러 시작:", "일시정지", "중지 요청", "자동등록은 일시정지", "전체 설정 저장",
        "CSV 가져오기:", "CSV 내보내기:", "내 등록글 조회:", "조회 결과:",
        "내 등록글 조회 취소", "등록글 자동 조회 완료", "등록글 자동 조회 취소",
        "선택 물품", "서버별 복제 완료:", "표 상단", "물품 목록 열 너비",
        "선택한 기존 등록글", "일괄수정", "물품 설정 저장:", "사이트 진단 파일 저장",
        "주의: 결과 확인 대기", "보류 사유:", "보류 자동 확인:", "보류 자동 해제:",
        "결과 확인이 필요한 물품", "[미제출 조회시험",
        "B VPN 접속 대기:", "B VPN 독립 외부 IP 재확인 완료", "B 자동등록 전체를 90초"
    };

    public static LogPresentationResult Format(string? message)
    {
        string value = message ?? "";
        if (TrySummary(value, "재등록 완료 요약:", "✓ 재등록 성공", false, out var result) ||
            TrySummary(value, "등록 완료 요약:", "✓ 등록 성공", false, out result) ||
            TrySummary(value, "재등록 실패 요약:", "✕ 재등록 실패", true, out result) ||
            TrySummary(value, "등록 실패 요약:", "✕ 등록 실패", true, out result) ||
            TrySummary(value, "재등록 대기 요약:", "◷ 서버 제한 대기", false, out result) ||
            TrySummary(value, "시험 완료 요약:", "○ 시험 완료", false, out result) ||
            TrySummary(value, "작업 중지 요약:", "■ 작업 중지", false, out result))
            return result;

        bool standaloneError = new[]
        {
            "등록글 자동 조회 실패:", "내 등록글 페이지 조회 오류:",
            "사이트 진단 실패:", "오류 로그 복사 실패:", "선택 모드 실패:",
            "등록글 조회 페이지 오류:", "실행 거절:", "B VPN 연결 재확인 실패:"
        }.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        if (standaloneError) return new LogPresentationResult(true, true, Compact(value));

        bool informational = AdministrativeMessages.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        return informational
            ? new LogPresentationResult(true, false, Compact(value))
            : new LogPresentationResult(false, false, "");
    }

    private static bool TrySummary(string message, string prefix, string label, bool isError,
        out LogPresentationResult result)
    {
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            result = default;
            return false;
        }
        result = new LogPresentationResult(true, isError,
            label + " · " + Compact(message[prefix.Length..].Trim(), 155));
        return true;
    }

    private static string Compact(string text, int maxLength = 195)
    {
        string clean = string.Join(" ", (text ?? "").Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > maxLength ? clean[..maxLength] + "…" : clean;
    }
}
