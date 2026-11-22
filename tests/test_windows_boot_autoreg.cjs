'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const get = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');

const startup = get('WindowsAutoStart.cs');
const program = get('Program.cs');
const main = get('MainForm.cs');
const boot = get('MainForm.WindowsStartup.cs');
const split = get('MainForm.SplitView.cs');
const auto = get('AutomationForm.cs');

// Register only in the interactive user's login startup; no services, admin or passwords.
assert.match(startup, /public bool Enabled \{ get; set; \} = true/);
assert.match(startup, /Registry\.CurrentUser\.CreateSubKey\(RunKey, writable: true\)/);
assert.match(startup, /Software\\Microsoft\\Windows\\CurrentVersion\\Run/);
assert.match(startup, /--windows-startup/);
assert.match(startup, /Path\.GetFullPath\(exe\)/);
assert.match(startup, /key\.DeleteValue\(EntryName, throwOnMissingValue: false\)/);
assert.doesNotMatch(startup, /Registry\.LocalMachine|schtasks|ServiceController|password|token/i);

// No duplicate registration worker when the Windows run key and shortcut overlap.
assert.match(program, /new Mutex\(true, @"Local\\LuzenBrowserReplica\.SingleInstance", out bool first\)/);
assert.match(program, /if \(!first\)/);

// Startup checks for verified automatic updates before starting site work.
assert.match(main, /_ = InitializeWindowsBootAsync\(\)/);
assert.match(main, /settingsMenu\.DropDownItems\.Add\(BuildWindowsStartupMenu\(\)\)/);
assert.match(boot, /await CheckForUpdateAsync\(automatic: true\)/);
assert(boot.indexOf('await CheckForUpdateAsync(automatic: true)') <
       boot.indexOf('await StartSelectedSystemsAsync(silentStartup: true)'));
assert.match(boot, /WindowsAutoStart\.SetEnabled\(_windowsBoot\.Enabled\)/);

// Existing A/B target settings are respected. C is never silently auto-started.
assert.match(split, /if \(_startTargets\.A\)/);
assert.match(split, /if \(bReady && _startTargets\.B\)/);
assert.match(split, /_moduleA\.StartAutomationOnBoot\(\)/);
assert.match(split, /_moduleB\.StartAutomationOnBoot\(\)/);
assert.match(split, /if \(!silentStartup && _activeSystem == "C"/);
assert.match(auto, /public void StartAutomationOnBoot\(\) => StartScheduler\(silent: true\)/);

// Never submit B via an unverified VPN, never show login prompts at boot.
assert.match(split, /await EnsureBReadyAsync\(silentStartup\)/);
assert.match(split, /if \(!silentStartup\) OpenSystemNetworkSettings\("B"\)/);
assert.match(split, /if \(!silentStartup\)[\s\S]*MessageBox\.Show\(this, "B VPN/);
assert.match(boot, /_bootBRetryTimer = new\(\) \{ Interval = 60_000 \}/);
assert.match(boot, /await EnsureBReadyAsync\(silentStartup: true\)/);
assert.match(split, /_bootCancelledByUser = true/);
assert.match(split, /CancelBootBRetry\(\)/);
assert.doesNotMatch(boot, /ExecuteJobAsync|CoreWebView2|Submit|MarkAttempt/);

console.log('PASS Windows user-login auto launch, single instance, update-first A+B startup, safe VPN retry and off toggle');
