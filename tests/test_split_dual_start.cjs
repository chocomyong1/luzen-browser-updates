'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const get = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const main = get('MainForm.cs');
const split = get('MainForm.SplitView.cs');
const auto = get('AutomationForm.cs');
const splitAuto = get('AutomationForm.SplitStatus.cs');
const tables = get('AutomationForm.ModernTables.cs');
const logPresentation = get('LogPresentation.cs');

// The default top-level start targets both existing isolated schedulers,
// and the user's A/B preference persists across restarts.
assert.match(split, /public bool A \{ get; set; \} = true;/);
assert.match(split, /public bool B \{ get; set; \} = true;/);
assert.match(split, /JsonSerializer\.Deserialize<AutomaticStartTargets>/);
assert.match(split, /File\.Move\(temp, StartTargetsPath, true\)/);
assert.match(main, /"자동 시작", \(\) => _ = StartSelectedSystemsAsync\(\)/);
const start = split.slice(split.indexOf('private async Task StartSelectedSystemsAsync()'), split.indexOf('private void PauseSelectedSystems()'));
assert(start.includes('if (_startTargets.A) _moduleA.StartAutomationFromHeader();'));
assert(start.includes('await EnsureBReadyAsync()'));
assert(start.includes('if (bReady && _startTargets.B) _moduleB.StartAutomationFromHeader();'));
assert(start.includes('if (_activeSystem == "C" && !_splitActive)'));

// B cannot start or become visible with A's route. Stop/pause affects both.
const ensure = split.slice(split.indexOf('private async Task<bool> EnsureBReadyAsync()'), split.indexOf('private async Task StartSelectedSystemsAsync()'));
assert(ensure.includes('DualSystemNetwork.IsRouteVerified("B")'));
assert(ensure.includes('await DualSystemNetwork.VerifyBRouteAsync()'));
assert(ensure.indexOf('DualSystemNetwork.IsRouteVerified("B") || IsDisposed') < ensure.indexOf('_moduleB.Show()'));
assert.match(split, /_moduleA\.PauseAutomationFromHeader\(\);[\s\S]*_moduleB\.PauseAutomationFromHeader\(\)/);
assert.match(split, /_moduleA\.StopAutomationFromHeader\(\);[\s\S]*_moduleB\.StopAutomationFromHeader\(\)/);

// The UI splits the same two existing forms; no duplicate browser/scheduler.
assert.match(main, /"스플릿", \(\) => _ = ToggleSplitViewAsync\(\)/);
assert.match(split, /new SplitContainer/);
assert.match(split, /split\.Panel1\.Controls\.Add\(_moduleA\)/);
assert.match(split, /split\.Panel2\.Controls\.Add\(_moduleB\)/);
assert.match(split, /split\.SplitterDistance = \(split\.ClientSize\.Width - split\.SplitterWidth\) \/ 2/);
const restore = split.slice(split.indexOf('private void DisableSplitView()'), split.indexOf('private void ExitSplitForEditor('));
assert(restore.indexOf('Controls.Remove(_moduleA)') < restore.indexOf('_splitContainer.Dispose()'));
assert(restore.indexOf('Controls.Remove(_moduleB)') < restore.indexOf('_splitContainer.Dispose()'));
assert.match(splitAuto, /RequestSingleViewFromSplit/);
assert.match(auto, /if \(_splitStatusMode\) RequestSingleViewFromSplit\?\.Invoke\(_system\)/);
for (const field of ['Enabled', 'Server', 'Price', 'Title', 'NextDueLocal', 'LastStatus'])
  assert(splitAuto.includes(`"${field}"`), `split missing ${field}`);
assert.match(splitAuto, /_preSplitAutoColumns\.Clear\(\)/);
assert.match(tables, /_splitStatusMode &&[\s\S]*_preSplitAutoColumns\.TryGetValue/);
assert.match(auto, /_segmentedNavigation = BuildSegmentedNavigation\(\)/);

// Detailed elapsed-time information remains in raw logs, but is not shown
// in the routine compact log presentation.
assert(auto.includes('AppendLog($"처리 시간 요약:'));
assert(!logPresentation.includes('"처리 시간 요약:"'));

console.log('PASS A+B independent group control, guarded split, reversible layout and quiet logs');
