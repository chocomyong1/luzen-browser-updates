using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace LuzenBrowserReplica;

// Only known app-owned, small config files are included. No browser cookies,
// plaintext passwords, logs, machine-wide configuration, or arbitrary paths.
// Stored password/token fields remain encrypted by Windows DPAPI.
internal static class UserSettingsSnapshots
{
    private static readonly string[] RootNames =
    {
        "automation.json", "itemmania_account.json", "grid-columns-v17.json",
        "preferences.json", "menus.json", "schedule.json", "AutoStart.cfg",
        "windows-auto-start.json", "automatic-start-targets.json", "app-updates.json",
        "telegram-alerts.json", "system-b-network.json", "system-c-network.json"
    };
    private static readonly string[] ProfileNames =
    { "automation.json", "itemmania_account.json", "grid-columns-v17.json" };
    private static readonly HashSet<string> AllowedEntries = BuildAllowedEntries();
    public static string DirectoryPath => Path.Combine(Settings.UserDirectory, "Backups");
    private static string RestoreRequestPath => Path.Combine(DirectoryPath, "restore-request.json");
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const long MaxTotalBytes = 25 * 1024 * 1024;

    private static HashSet<string> BuildAllowedEntries()
    {
        var entries = new HashSet<string>(RootNames, StringComparer.Ordinal);
        foreach (string system in new[] { "SystemB", "SystemC" })
            foreach (string file in ProfileNames) entries.Add(system + "/" + file);
        return entries;
    }

    private static string SourcePath(string entry) => Path.Combine(Settings.UserDirectory,
        entry.Replace('/', Path.DirectorySeparatorChar));

    private static void ValidateContent(string filename, Stream stream)
    {
        if (filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object &&
                document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("백업 설정 형식이 올바르지 않습니다.");
        }
        else if (filename.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
            _ = XDocument.Load(stream);
    }

    public static string[] ListSnapshots()
    {
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
        return Directory.GetFiles(DirectoryPath, "Luzen-*.zip", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(name => name is not null)
            .Select(name => name!).OrderByDescending(name => name, StringComparer.Ordinal).ToArray();
    }

    public static string CreateSnapshot(string reason, bool daily = false)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (daily)
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            if (ListSnapshots().Any(file => file.StartsWith("Luzen-" + today + "-", StringComparison.Ordinal) &&
                file.Contains("-daily-", StringComparison.Ordinal)))
                return "";
        }
        if (reason is not ("daily" or "pre-update" or "manual" or "pre-restore"))
            throw new ArgumentException("알 수 없는 설정 백업 종류입니다.");
        string filename = $"Luzen-{DateTime.Now:yyyyMMdd-HHmmss}-{reason}-{Guid.NewGuid():N}.zip";
        string destination = Path.Combine(DirectoryPath, filename);
        string temp = destination + ".tmp";
        try
        {
            long total = 0;
            int copied = 0;
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (string entryName in AllowedEntries.OrderBy(s => s, StringComparer.Ordinal))
                {
                    string source = SourcePath(entryName);
                    if (!File.Exists(source)) continue;
                    var info = new FileInfo(source);
                    if (info.Length > MaxFileBytes || total + info.Length > MaxTotalBytes)
                        throw new InvalidDataException("설정 백업 파일의 허용 크기를 초과했습니다.");
                    try
                    {
                        using var read = File.OpenRead(source);
                        ValidateContent(entryName, read);
                    }
                    catch (Exception) when (!entryName.EndsWith("automation.json", StringComparison.Ordinal))
                    {
                        // Do not block automatic updates because an unrelated
                        // optional UI preference is malformed. Preserve the
                        // last valid backup for that setting, if any.
                        continue;
                    }
                    archive.CreateEntryFromFile(source, entryName, CompressionLevel.Optimal);
                    total += info.Length;
                    copied++;
                }
                if (copied == 0) throw new InvalidDataException("백업할 설정 파일이 없습니다.");
                var metadata = archive.CreateEntry("_manifest.json", CompressionLevel.Optimal);
                using var writer = new StreamWriter(metadata.Open());
                writer.Write(JsonSerializer.Serialize(new { Version = AppUpdateService.CurrentVersion,
                    CreatedUtc = DateTime.UtcNow, Reason = reason, Files = copied }));
            }
            File.Move(temp, destination);
            return destination;
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    public static void RequestRestore(string snapshotFileName)
    {
        if (!ListSnapshots().Contains(snapshotFileName, StringComparer.Ordinal))
            throw new InvalidDataException("선택한 백업을 찾을 수 없습니다.");
        Directory.CreateDirectory(DirectoryPath);
        string temp = RestoreRequestPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new { Snapshot = snapshotFileName }));
        File.Move(temp, RestoreRequestPath, overwrite: true);
    }

    public static bool ApplyPendingRestoreOnStartup()
    {
        if (!File.Exists(RestoreRequestPath)) return false;
        using var request = JsonDocument.Parse(File.ReadAllText(RestoreRequestPath));
        string snapshot = request.RootElement.GetProperty("Snapshot").GetString() ?? "";
        if (!ListSnapshots().Contains(snapshot, StringComparer.Ordinal))
            throw new InvalidDataException("복구를 요청한 백업 파일을 찾을 수 없습니다.");

        string staging = Path.Combine(DirectoryPath, "restore-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            long total = 0;
            int recovered = 0;
            using (var archive = ZipFile.OpenRead(Path.Combine(DirectoryPath, snapshot)))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName == "_manifest.json") continue;
                    if (!AllowedEntries.Contains(entry.FullName) || entry.Length > MaxFileBytes ||
                        total + entry.Length > MaxTotalBytes)
                        throw new InvalidDataException("백업 내부 경로나 파일 크기가 허용되지 않습니다.");
                    string staged = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    entry.ExtractToFile(staged);
                    using (var input = File.OpenRead(staged)) ValidateContent(entry.FullName, input);
                    if (entry.FullName.EndsWith("automation.json", StringComparison.Ordinal))
                    {
                        var config = JsonSerializer.Deserialize<AutomationConfiguration>(File.ReadAllText(staged));
                        if (config?.Jobs is null || config.Workflows is null)
                            throw new InvalidDataException("백업의 자동등록 물품 정보가 올바르지 않습니다.");
                    }
                    total += entry.Length;
                    recovered++;
                }
            }
            if (recovered == 0 || !File.Exists(Path.Combine(staging, "automation.json")))
                throw new InvalidDataException("백업에 A 자동등록 설정이 없습니다.");

            // Never lose the settings from just before this restore; if the
            // chosen snapshot is old, the user can still recover the newer copy.
            CreateSnapshot("pre-restore");
            foreach (string entry in AllowedEntries)
            {
                string staged = Path.Combine(staging, entry.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(staged)) continue;
                string dest = SourcePath(entry);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(staged, dest, overwrite: true);
                if (entry.EndsWith("automation.json", StringComparison.Ordinal))
                {
                    var config = JsonSerializer.Deserialize<AutomationConfiguration>(File.ReadAllText(dest))
                        ?? throw new InvalidDataException("백업 자동등록 목록을 읽을 수 없습니다.");
                    // Older backups cannot prove what was actually submitted
                    // after they were made. No automatic posting after restore.
                    foreach (var job in config.Jobs)
                    {
                        job.Enabled = false;
                        if (job.InitialCompleted || job.PendingRepostAttemptUtc.HasValue)
                        {
                            job.RequiresReview = true;
                            job.NextDueUtc = DateTime.MaxValue;
                            job.LastStatus = "백업 복원: 사이트 확인 필요";
                        }
                    }
                    config.StorageDirectory = Path.GetDirectoryName(dest)!;
                    config.Save();
                }
            }
            WindowsAutoStart.SetEnabled(false);
            File.Delete(RestoreRequestPath);
            return true;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }
}
