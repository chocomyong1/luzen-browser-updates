using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LuzenBrowserReplica;

// CSV interchange for listings. Imports are intentionally DISABLED and have
// fresh IDs so a spreadsheet can never silently submit an imported listing.
internal static class CsvJobStorage
{
    private static readonly string[] Headers =
    {
        "유형", "게임", "서버", "제목", "수량", "단위", "금액", "캐릭터", "상세설명", "진입URL", "주기(분)", "자동실행", "물품구분", "매매유형", "매매단위", "최소수량", "최대수량", "자동감소", "즉시거래", "캐릭터직업", "캐릭터레벨", "서브ID", "사이트ID", "유료등록",
        "등록번호", "원래거래", "기존글가져오기", "등록완료", "자동재등록"
    };

    public static void Export(string path, IEnumerable<ListingJob> jobs)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(",", Headers.Select(Escape)));
        foreach (var job in jobs)
        {
            var columns = new[]
            {
                job.Operation, job.Game, job.Server, job.Title, job.Quantity, job.Unit,
                job.Price, job.Character, job.Description, job.EntryUrl,
                job.IntervalMinutes.ToString(CultureInfo.InvariantCulture), job.Enabled ? "Y" : "N",
                job.ItemCategory, job.TradingMethod, job.TradingUnit,
                job.MinQuantity, job.MaxQuantity, job.AutoDecrease ? "Y" : "N", job.InstantTransaction ? "Y" : "N",
                job.CharacterClass, job.CharacterLevel, job.SubId, job.SiteId, job.PaidRegistration ? "Y" : "N",
                job.ListingNumber, job.OriginalTradeSide, job.ImportedFromSite ? "Y" : "N",
                job.InitialCompleted ? "Y" : "N", job.AutoRepost ? "Y" : "N"
            };
            writer.WriteLine(string.Join(",", columns.Select(Escape)));
        }
    }

    public static List<ListingJob> Import(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        var rows = Parse(reader.ReadToEnd());
        if (rows.Count == 0) throw new InvalidDataException("빈 CSV 파일입니다.");
        var headers = rows[0];
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) index[headers[i].Trim().TrimStart('\ufeff')] = i;
        foreach (var key in new[] { "유형", "게임", "서버", "제목" })
            if (!index.ContainsKey(key)) throw new InvalidDataException("필수 열이 없습니다: " + key);

        var result = new List<ListingJob>();
        for (int rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string Read(string key, string fallback = "")
            {
                return index.TryGetValue(key, out var i) && i < row.Count
                    ? UndoExcelEscape(row[i]).Trim() : fallback;
            }
            var operation = Read("유형", "판매등록");
            if (operation is not ("판매등록" or "구매등록" or "재등록"))
                throw new InvalidDataException($"{rowNumber + 1}행: 유형은 판매등록/구매등록/재등록 중 하나여야 합니다.");
            var existingListing = Read("기존글가져오기") == "Y";
            var categoryText = Read("물품구분", operation == "재등록" ? "" : "아이템");
            var category = ItemManiaFieldCatalog.CanonicalCategory(categoryText);
            if (category is null && (operation != "재등록" || !string.IsNullOrWhiteSpace(categoryText)))
                throw new InvalidDataException($"{rowNumber + 1}행: 물품구분은 머니/아이템/계정/기타 중 하나여야 합니다.");
            var method = Read("매매유형", operation == "재등록" ? "" : "분할");
            if (!ItemManiaFieldCatalog.IsTradingMethod(method) && (operation != "재등록" || !string.IsNullOrWhiteSpace(method)))
                throw new InvalidDataException($"{rowNumber + 1}행: 거래유형은 일반/분할만 확인됐습니다. 다른 유형은 수동 검증이 필요합니다.");
            var title = Read("제목"); var game = Read("게임"); var server = Read("서버");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(server))
                throw new InvalidDataException($"{rowNumber + 1}행: 제목과 서버를 입력하세요.");
            if (!GameServerCatalog.IsSupported(game, server))
                throw new InvalidDataException($"{rowNumber + 1}행: 지원하지 않는 게임/서버: {game} / {server}. 서버 목록의 이름을 사용하세요.");
            if (!int.TryParse(Read("주기(분)", "30"), out var period) || period < 1 || period > 10080)
                throw new InvalidDataException($"{rowNumber + 1}행: 주기(분)는 1~10080 사이여야 합니다.");
            result.Add(new ListingJob
            {
                // Existing posts are imported as REPOST, never as a new sale/buy.
                Operation = existingListing ? "재등록" : operation, Game = game, Server = GameServerCatalog.CanonicalServer(game, server)!, Title = title,
                Quantity = Read("수량", "1"), Unit = Read("단위", "개"),
                Price = Read("금액"), Character = Read("캐릭터"), Description = Read("상세설명"),
                EntryUrl = Read("진입URL"), IntervalMinutes = period,
                ItemCategory = category ?? "", TradingMethod = method,
                ListingNumber = Read("등록번호"), OriginalTradeSide = Read("원래거래"),
                ImportedFromSite = existingListing, ImportedUtc = existingListing ? DateTime.UtcNow : null,
                InitialCompleted = existingListing || Read("등록완료") == "Y",
                AutoRepost = existingListing || Read("자동재등록", "Y") == "Y",
                RequiresReview = existingListing || Read("등록완료") == "Y",
                NextDueUtc = existingListing ? DateTime.MaxValue : DateTime.MinValue,
                TradingUnit = Read("매매단위", "없음"), MinQuantity = Read("최소수량", "1"),
                MaxQuantity = Read("최대수량", "9999"), AutoDecrease = Read("자동감소") == "Y",
                InstantTransaction = Read("즉시거래") == "Y", CharacterClass = Read("캐릭터직업"),
                CharacterLevel = Read("캐릭터레벨"),
                SubId = Read("서브ID"), SiteId = Read("사이트ID"), PaidRegistration = Read("유료등록") == "Y",
                Enabled = false, // no unattended submission on CSV import
                LastStatus = existingListing ? "CSV 기존글: 재등록 전 확인 필요" : "CSV 가져옴 (자동 해제)"
            });
        }
        return result;
    }

    private static string Escape(string? value)
    {
        value ??= "";
        // CSV quoting alone does not prevent spreadsheet formula execution.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        if (value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string UndoExcelEscape(string value)
        => value.Length > 1 && value[0] == '\'' && "=+-@\t\r\n".Contains(value[1]) ? value[1..] : value;

    // RFC-4180-style quoted fields, commas, newlines and escaped quotes.
    private static List<List<string>> Parse(string content)
    {
        var rows = new List<List<string>>(); var row = new List<string>();
        var sb = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < content.Length; i++)
        {
            char ch = content[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < content.Length && content[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"' && sb.Length == 0) quoted = true;
            else if (ch == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                row.Add(sb.ToString()); sb.Clear(); rows.Add(row); row = new List<string>();
            }
            else sb.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV 따옴표가 닫히지 않았습니다.");
        if (row.Count > 0 || sb.Length > 0) { row.Add(sb.ToString()); rows.Add(row); }
        return rows;
    }
}
