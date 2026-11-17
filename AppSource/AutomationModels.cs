using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LuzenBrowserReplica;

internal sealed class ListingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SelectedForBulk { get; set; }
    public string SubId { get; set; } = "";
    public string SiteId { get; set; } = "";
    public bool PaidRegistration { get; set; }
    public bool Enabled { get; set; }
    public string Operation { get; set; } = "판매등록"; // 판매등록 / 구매등록 / 재등록
    public string Game { get; set; } = "";
    public string Server { get; set; } = "";
    // Preserve legacy properties for backwards-compatible JSON reading;
    // do not render or submit unverified ItemInfoGroup / ItemInfoDetail values.
    public string ItemCategory { get; set; } = "아이템";
    public string ItemInfoGroup { get; set; } = "";
    public string ItemInfoDetail { get; set; } = "";
    public string TradingMethod { get; set; } = "분할";
    public string TradingUnit { get; set; } = "없음";
    public string MinQuantity { get; set; } = "1";
    public string MaxQuantity { get; set; } = "9999";
    public bool AutoDecrease { get; set; } = false;
    public bool InstantTransaction { get; set; } = false;
    public string CharacterClass { get; set; } = "";
    public string CharacterLevel { get; set; } = "";
    public string ListingMacro { get; set; } = "직접설정";
    public string Title { get; set; } = "";
    public string Quantity { get; set; } = "1";
    public string Unit { get; set; } = "개";
    public string Price { get; set; } = "";
    public string Description { get; set; } = "";
    public string Character { get; set; } = "";
    public string EntryUrl { get; set; } = ""; // 재등록 대상 본인 물품 URL (신규 등록 URL과 분리)
    public string SourceListUrl { get; set; } = ""; // 내 등록글을 실제로 찾은 관리 목록 페이지 (페이지 번호 포함)
    public string ListingNumber { get; set; } = ""; // 사이트 기존 등록글 고유번호
    public string OriginalTradeSide { get; set; } = ""; // 판매 / 구매 (기존글 가져오기)
    public bool ImportedFromSite { get; set; }
    public DateTime? ImportedUtc { get; set; }
    // After the first verified sale/buy post, resubmit the SAME listing using its
    // verified management URL. Never create a new listing instead of reposting.
    public bool AutoRepost { get; set; } = true;
    public int IntervalMinutes { get; set; } = 30;
    public bool InitialCompleted { get; set; }
    public bool RequiresReview { get; set; }
    // Persist the pre-submit site timestamp so an interrupted repost can be
    // reconciled read-only by exact listing ID on the next My Room scan.
    public string PendingRepostBeforeStamp { get; set; } = "";
    public DateTime? PendingRepostAttemptUtc { get; set; }
    public DateTime NextDueUtc { get; set; } = DateTime.MinValue;
    public DateTime? LastSucceededUtc { get; set; }
    public string LastStatus { get; set; } = "대기";
    [System.Text.Json.Serialization.JsonIgnore]
    public string CharacterDisplay => string.IsNullOrWhiteSpace(Character) && ImportedFromSite ? "미수집" : Character;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) && ImportedFromSite ? "미수집" : Description;
    // The grid's status is the current state, not the most recent diagnostic
    // message. Keep LastStatus unmodified for logs, recovery, and support.
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusDisplay
    {
        get
        {
            if (RequiresReview) return "확인 필요";
            if (LastStatus == "실행 중") return "실행 중";
            if (LastStatus.StartsWith("제출 중", StringComparison.Ordinal)) return "제출 확인 중";
            if (LastStatus.Contains("오류", StringComparison.Ordinal) ||
                LastStatus.Contains("실패", StringComparison.Ordinal)) return "오류";
            if (LastStatus.StartsWith("시험 완료", StringComparison.Ordinal)) return "시험 완료";
            if (!Enabled) return LastStatus == "중지됨" ? "중지됨" : "자동 꺼짐";
            if (InitialCompleted && !AutoRepost && Operation != "재등록") return "완료";
            if (NextDueUtc == DateTime.MaxValue) return "예약 필요";
            if (NextDueUtc == DateTime.MinValue) return "실행 대기";
            return "예약됨";
        }
    }
    public string ViewTitle => $"[{Operation}] {Title}";
    public string NextDueLocal => RequiresReview ? "확인 필요" :
        InitialCompleted && !AutoRepost && Operation != "재등록" ? "완료" :
        !Enabled ? "자동 꺼짐" :
        NextDueUtc == DateTime.MaxValue ? "예약 필요" :
        NextDueUtc == DateTime.MinValue ? "즉시" : NextDueUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
}

internal sealed class AutomationStep
{
    public string Action { get; set; } = "입력";
    public string Selector { get; set; } = "";
    public string Value { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 15;
}

internal sealed class AutomationWorkflow
{
    public string Operation { get; set; } = "판매등록";
    public string EntryUrl { get; set; } = "https://www.itemmania.com/sell/";
    public string SuccessSelector { get; set; } = "";
    public string SuccessUrlContains { get; set; } = "";
    // Optional: unique link on the SUCCESS page pointing to the just posted item.
    // If empty or ambiguous, the job stops pending manual URL confirmation.
    public string RepostLinkSelector { get; set; } = "";
    public List<AutomationStep> Steps { get; set; } = new();
}

internal sealed class AutomationConfiguration
{
    // This is a real browser workflow runner, not the original LuzenSoft server connector.
    public string AllowedHost { get; set; } = "www.itemmania.com";
    public int MaxSubmissionsPerHour { get; set; } = 30;
    public int MaxListingsPerServer { get; set; } = 30;
    public int MinimumServerSeconds { get; set; } = 21;
    public int MinimumGlobalSeconds { get; set; } = 21;
    public int DetailEnrichmentCursor { get; set; }
    public List<DateTime> SubmissionAttemptsUtc { get; set; } = new();
    public Dictionary<string, DateTime> LastServerAttemptUtc { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ListingJob> Jobs { get; set; } = new();
    public List<AutomationWorkflow> Workflows { get; set; } = new();

    public AutomationWorkflow WorkflowFor(string operation)
    {
        var workflow = Workflows.FirstOrDefault(x => x.Operation == operation);
        if (workflow is not null) return workflow;
        workflow = MakeWorkflow(operation);
        Workflows.Add(workflow);
        return workflow;
    }

    private static AutomationWorkflow MakeWorkflow(string type)
    {
        // @auto: aliases are resolved from VISIBLE, unambiguous field labels
        // on the CURRENT page. If a field cannot be uniquely matched, fail closed.
        // These are adaptive mappings, NOT claimed to be verified live selectors.
        var workflow = new AutomationWorkflow
        {
            Operation = type,
            SuccessSelector = "@auto:success",
            EntryUrl = type == "판매등록" ? "https://trade.itemmania.com/sell/" :
                type == "구매등록" ? "https://trade.itemmania.com/buy/" : "https://trade.itemmania.com/myroom/"
        };
        workflow.Steps.Add(new AutomationStep { Action = "이동", Value = "{EntryUrl}" });
        if (type == "재등록")
        {
            workflow.Steps.Add(new AutomationStep { Action = "제출", Selector = "@auto:repost", Value = "" });
            // Optional second confirmation button requires manual site verification.
        }
        else
        {
            foreach (var key in new[] { "Game", "Server", "ItemCategory", "TradingMethod" })
                workflow.Steps.Add(new AutomationStep { Action = "선택", Selector = "@auto:" + key, Value = "{" + key + "}" });
            // Character name is explicitly present on the original seller/buyer form.
            // A unique, verified field is still required; no arbitrary CSS guess.
            foreach (var key in new[] { "Quantity", "Price", "Character", "Title", "Description" })
                workflow.Steps.Add(new AutomationStep { Action = "입력", Selector = "@auto:" + key, Value = "{" + key + "}" });
            // Only used when TradingMethod == 분할; fields must be uniquely identifiable.
            foreach (var key in new[] { "MinQuantity", "MaxQuantity" })
                workflow.Steps.Add(new AutomationStep { Action = "입력", Selector = "@auto:" + key, Value = "{" + key + "}" });
            workflow.Steps.Add(new AutomationStep { Action = "체크", Selector = "@auto:AutoDecrease", Value = "{AutoDecrease}" });
            workflow.Steps.Add(new AutomationStep { Action = "제출", Selector =
                type == "판매등록" ? "@auto:submitSell" : "@auto:submitBuy" });
        }
        return workflow;
    }

    public static AutomationConfiguration Load()
    {
        var path = Path.Combine(Settings.UserDirectory, "automation.json");
        // Recover only from the last confirmed backup if the primary is damaged.
        // Never silently erase a user's jobs when both saved files are unreadable.
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var loaded = JsonSerializer.Deserialize<AutomationConfiguration>(File.ReadAllText(candidate));
                if (loaded is null) continue;
                loaded.Workflows ??= new(); loaded.Jobs ??= new();
                loaded.SubmissionAttemptsUtc ??= new(); loaded.LastServerAttemptUtc ??= new();
                // Legacy/manual edits may contain impossible or unsafe limits.
                // Keep the persisted configuration within the enforced bounds.
                loaded.MaxSubmissionsPerHour = Math.Clamp(loaded.MaxSubmissionsPerHour, 1, 1000);
                loaded.MaxListingsPerServer = Math.Clamp(loaded.MaxListingsPerServer, 1, 500);
                loaded.MinimumServerSeconds = Math.Clamp(loaded.MinimumServerSeconds, 21, 3600);
                loaded.MinimumGlobalSeconds = Math.Max(21, loaded.MinimumGlobalSeconds);
                loaded.SubmissionAttemptsUtc = loaded.SubmissionAttemptsUtc
                    .Where(dt => dt > DateTime.UtcNow.AddHours(-1) && dt <= DateTime.UtcNow).ToList();
                // Older imports could be switched ON while still carrying the
                // disabled-only MaxValue sentinel, leaving them in limbo.
                // Do not change genuinely uncertain submission checkpoints.
                foreach (var job in loaded.Jobs.Where(j => j.Enabled && !j.RequiresReview &&
                    !j.PendingRepostAttemptUtc.HasValue && j.NextDueUtc == DateTime.MaxValue &&
                    (j.Operation == "재등록" || j.AutoRepost || !j.InitialCompleted)))
                    job.NextDueUtc = DateTime.UtcNow;
                foreach (var kind in new[] { "판매등록", "구매등록", "재등록" })
                {
                    var wf = loaded.WorkflowFor(kind);
                    // Apply V14 split-quantity steps to default auto workflows only;
                    // never rewrite a user's hand-configured CSS or button sequence.
                    if (kind != "재등록" && wf.Steps.Any(x => x.Selector == "@auto:Quantity") &&
                        wf.Steps.Any(x => x.Selector is "@auto:submit" or "@auto:submitSell" or "@auto:submitBuy") &&
                        wf.Steps.Where(x => x.Action is "입력" or "선택" or "제출" or "체크").All(x => x.Selector.StartsWith("@auto:", StringComparison.Ordinal)) &&
                        !wf.Steps.Any(x => x.Value == "{MinQuantity}"))
                    {
                        int index = wf.Steps.FindIndex(x => x.Action == "제출");
                        if (index >= 0)
                        {
                            wf.Steps.Insert(index++, new AutomationStep { Action = "입력", Selector = "@auto:MinQuantity", Value = "{MinQuantity}" });
                            wf.Steps.Insert(index++, new AutomationStep { Action = "입력", Selector = "@auto:MaxQuantity", Value = "{MaxQuantity}" });
                            wf.Steps.Insert(index, new AutomationStep { Action = "체크", Selector = "@auto:AutoDecrease", Value = "{AutoDecrease}" });
                        }
                    }
                    if (kind != "재등록" && wf.Steps.Any(x => x.Selector == "@auto:Quantity") &&
                        wf.Steps.Any(x => x.Selector == "@auto:submit") &&
                        wf.Steps.Where(x => x.Action is "입력" or "선택" or "제출" or "체크").All(x => x.Selector.StartsWith("@auto:", StringComparison.Ordinal)))
                        foreach (var st in wf.Steps.Where(x => x.Action == "제출" && x.Selector == "@auto:submit"))
                            st.Selector = kind == "판매등록" ? "@auto:submitSell" : "@auto:submitBuy";
                    // Legacy template has ONLY empty CSS selectors: replace those
                    // templates without touching any customised workflow.
                    if (wf.Steps.Count > 0 && wf.Steps.Where(x => x.Action is "입력" or "선택" or "제출" or "클릭")
                        .All(x => string.IsNullOrWhiteSpace(x.Selector)))
                    {
                        var upgraded = MakeWorkflow(kind);
                        wf.Steps = upgraded.Steps;
                        if (string.IsNullOrWhiteSpace(wf.SuccessSelector) && string.IsNullOrWhiteSpace(wf.SuccessUrlContains))
                            wf.SuccessSelector = "@auto:success";
                        // Preserve user-edited entry URLs and success markers.
                    }
                }
                if (candidate.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    // A backup cannot prove whether any in-flight submit succeeded.
                    foreach (var job in loaded.Jobs.Where(j => !j.InitialCompleted || j.Operation == "재등록" || j.AutoRepost))
                    { job.RequiresReview = true; job.LastStatus = "백업 복구: 사이트 확인 필요"; }
                }
                return loaded;
            }
            catch { /* Try the backup. */ }
        }
        // Protect corrupt files for diagnostics rather than overwriting them.
        if (File.Exists(path) || File.Exists(path + ".bak"))
            throw new InvalidDataException("자동등록 설정을 읽지 못했습니다. %LOCALAPPDATA%\\LuzenBrowserReplica 안의 automation.json 및 .bak 파일을 확인하세요. 기존 파일은 변경하지 않았습니다.");
        var config = new AutomationConfiguration();
        foreach (var kind in new[] { "판매등록", "구매등록", "재등록" }) config.WorkflowFor(kind);
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Settings.UserDirectory);
        var path = Path.Combine(Settings.UserDirectory, "automation.json");
        var temp = path + ".tmp";
        var serialized = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, serialized);
        // Validate the temporary copy before replacing a confirmed good file.
        if (JsonSerializer.Deserialize<AutomationConfiguration>(File.ReadAllText(temp)) is null)
            throw new InvalidDataException("저장할 설정 검증에 실패했습니다.");
        if (File.Exists(path))
        {
            // Do not replace a valid recovery copy with a corrupt primary.
            try
            {
                if (JsonSerializer.Deserialize<AutomationConfiguration>(File.ReadAllText(path)) is not null)
                    File.Copy(path, path + ".bak", true);
            }
            catch { /* preserve previously valid .bak */ }
        }
        File.Move(temp, path, true);
    }
}
