'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const src = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8').replace(/\r\n/g, '\n');
const runner = src('AutomationForm.cs');
const model = src('AutomationModels.cs');
const audit = src('AutomationForm.SiteAudit.cs');
const backup = src('UserSettingsSnapshots.cs');
const ui = src('MainForm.SettingsBackup.cs');
const main = src('MainForm.cs');
const boot = src('Program.cs');

// Only confirmed, exact owned listings may be audited; two discrepancies
// block that item. No audit code can submit, bypass quotas or alter prices.
assert.match(model, /LastSiteAuditUtc/);
assert.match(model, /SiteAuditMismatchCount/);
assert.match(audit, /requireRepostButton: false/);
assert.match(audit, /row\.Status != "found" \|\| string\.IsNullOrWhiteSpace\(row\.Stamp\)/);
assert.match(audit, /candidate\.SiteAuditMismatchCount >= 2/);
assert.match(audit, /candidate\.RequiresReview = true/);
assert.match(audit, /candidate\.NextDueUtc = DateTime\.MaxValue/);
assert.match(audit, /candidate\.LastStatus = "사이트 재등록 시각 재확인 필요"/);
assert.match(audit, /_nextSiteAuditUtc = DateTime\.UtcNow\.AddMinutes\(15\)/);
assert.match(runner, /await AuditOneConfirmedListingAsync\(\)/);
assert.match(runner, /_siteAuditRun\?\.Cancel\(\)/);
assert.doesNotMatch(audit, /MarkAttempt|ExecuteJobAsync|SubmitAsync|\.click\(|ApplySitePrice|PendingSitePriceTarget =/);

// Before every version install a recoverable copy is created. A restore is
// deferred until old WinForms forms have closed, and never auto-submits.
assert.match(main, /UserSettingsSnapshots\.CreateSnapshot\("daily", daily: true\)/);
assert.match(main, /UserSettingsSnapshots\.CreateSnapshot\("pre-update"\);\s*AppUpdateService\.StartExternalInstaller/);
assert.match(main, /moreMenu\.Items\.Add\("설정 백업·복원"/);
assert.match(ui, /CanInstallUpdateAcrossSystems/);
assert.match(ui, /UserSettingsSnapshots\.RequestRestore\(filename\)/);
assert.match(boot, /UserSettingsSnapshots\.ApplyPendingRestoreOnStartup\(\)/);
assert(boot.indexOf('ApplyPendingRestoreOnStartup()') < boot.indexOf('Settings.LoadPreferences()'));
assert.match(backup, /new HashSet<string>\(RootNames/);
assert.match(backup, /!AllowedEntries\.Contains\(entry\.FullName\)/);
assert.match(backup, /entry\.Length > MaxFileBytes/);
assert.match(backup, /CreateSnapshot\("pre-restore"\)/);
assert.match(backup, /job\.Enabled = false/);
assert.match(backup, /job\.RequiresReview = true/);
assert.match(backup, /WindowsAutoStart\.SetEnabled\(false\)/);
assert.doesNotMatch(backup, /ItemManiaWebView2|\.cookie|Cookies|logs\/|\.click\(|SubmitAsync/);
console.log('PASS safe read-only site timestamp audit and encrypted local backup/restore gates');
