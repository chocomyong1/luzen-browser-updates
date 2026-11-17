// A running job must keep its identity and price stable until its own
// registration/repost finishes; other jobs remain editable without pause.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const batch = read('AutomationForm.BatchEditorModern.cs');
const runner = read('AutomationForm.cs');

assert.doesNotMatch(batch.split('private void ShowModernBatchEditor()')[1].split('_jobsGrid.EndEdit()')[0],
  /_schedulerRunning/);
assert.match(batch, /_myListingsImportRunning \|\| \(_busy && _currentlyExecutingJob is null\)/);
assert.match(batch, /if \(!_busy\) SaveSelectedJob\(false\)/);
assert.match(batch, /ReferenceEquals\(x\.Item, _currentlyExecutingJob\)/);
assert.match(batch, /foreach \(var \(item, _\) in immediate\) write\(item, value\)/);
assert.match(batch, /_deferredExecutingJobEdits\.Add\(\(\) => write\(item, value\)\)/);
assert.match(batch, /_deferredExecutingJobEdits\.RemoveRange\(deferredStart/);
const runnerStart = runner.indexOf('_currentlyExecutingJob = job;');
const runnerEnd = runner.indexOf('foreach (var deferredEdit in _deferredExecutingJobEdits) deferredEdit();');
assert.ok(runnerStart > 0 && runnerEnd > runnerStart);
assert.match(runner.slice(runnerEnd, runnerEnd + 320), /_deferredExecutingJobEdits\.Clear\(\)/);
assert.match(runner.slice(runnerEnd, runnerEnd + 320), /_currentlyExecutingJob = null/);
console.log('PASS live batch editing: active submission isolated, other jobs editable');
