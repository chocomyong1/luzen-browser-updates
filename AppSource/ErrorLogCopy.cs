using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

// Build a short, copyable diagnostic from the local log. Nothing is uploaded.
internal static class ErrorLogCopy
{
    private static readonly Regex ErrorMarker = new(
        @"오류|실패|차단|예외|찾지 못|Exception|Error", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TelegramToken = new(
        @"(?<!\w)\d{6,12}:[A-Za-z0-9_-]{25,}(?!\w)", RegexOptions.Compiled);
    private static readonly Regex SecretValue = new(
        @"(?i)(?:비밀번호|봇\s*토큰|bot[_ -]?token|password|passwd|pwd|api[_ -]?key|secret)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled);
    private static readonly Regex BearerToken = new(
        @"(?i)Bearer\s+[^\s,;]+", RegexOptions.Compiled);

    public static string Build(IEnumerable<string> logLines, string version, DateTime copiedAt)
    {
        var lines = (logLines ?? Array.Empty<string>()).ToArray();
        var errors = Enumerable.Range(0, lines.Length)
            .Where(i => ErrorMarker.IsMatch(lines[i] ?? ""))
            .TakeLast(8).ToArray();
        var selected = new SortedSet<int>();
        if (errors.Length > 0)
        {
            foreach (int error in errors)
                for (int i = Math.Max(0, error - 8); i <= Math.Min(lines.Length - 1, error + 3); i++)
                    selected.Add(i);
        }
        else
        {
            for (int i = Math.Max(0, lines.Length - 60); i < lines.Length; i++)
                selected.Add(i);
        }

        var output = new StringBuilder();
        output.AppendLine("[Luzen 자동등록 오류 로그]");
        output.AppendLine("버전: " + version);
        output.AppendLine("복사 시각: " + copiedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        output.AppendLine(errors.Length > 0
            ? $"최근 오류 {errors.Length}건과 관련 작업 기록 (로컬 복사, 자동 전송 없음)"
            : "감지된 오류가 없어 최근 실행 기록을 복사함 (로컬 복사, 자동 전송 없음)");
        output.AppendLine();
        int previous = -1;
        foreach (int index in selected.TakeLast(140))
        {
            if (previous >= 0 && index > previous + 1) output.AppendLine("... 중간 기록 생략 ...");
            string line = lines[index] ?? "";
            if (line.Length > 1200) line = line[..1200] + " [긴 줄 생략]";
            line = SecretValue.Replace(line, "[비밀정보 숨김]");
            line = TelegramToken.Replace(line, "[봇 토큰 숨김]");
            line = BearerToken.Replace(line, "[인증정보 숨김]");
            output.AppendLine(line);
            previous = index;
        }
        return output.ToString();
    }
}
