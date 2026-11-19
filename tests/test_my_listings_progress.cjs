'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const src = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8').replace(/\r\n/g, '\n');
const main = src('MainForm.cs');
const ui = src('MainForm.MyListingsProgress.cs');
const importFlow = src('AutomationForm.MyListings.cs');

// The menu must await the real importer, and its state must stay visible
// over combined editing, hidden WebViews and other system tabs.
assert.match(main, /InitializeMyListingsProgressBanner\(\)/);
assert.match(main, /Controls\.Add\(_importProgressBanner\)/);
assert.match(main, /await _module\.ImportMyRegisteredListingsFromHeaderAsync\(\)/);
assert.match(ui, /_moduleA\.MyListingsImportProgress/);
assert.match(ui, /_moduleB\.MyListingsImportProgress/);
assert.match(ui, /_moduleC\.MyListingsImportProgress/);
assert.match(ui, /ProgressBarStyle\.Marquee/);
assert.match(ui, /RenderMyListingsProgressText\(\)/);
assert.match(ui, /CancelMyListingsImportFromHeader\(\)/);

// The importer reports actual work and outcome, including deferred import,
// login redirects, page count, detail count and every early return.
assert.match(importFlow, /MyListingsImportPhase\.Queued/);
assert.match(importFlow, /목록 \{result\.PagesVisited\}페이지 검사/);
assert.match(importFlow, /상세정보 확인 \{offset \+ 1\}\/\{batchSize\}건/);
assert.match(importFlow, /사이트 로그인 확인 대기/);
assert.match(importFlow, /MyListingsImportPhase\.Failed/);
assert.match(importFlow, /MyListingsImportPhase\.Completed/);
assert.match(importFlow, /MyListingsImportPhase\.Cancelled/);
assert.match(importFlow, /_myListingsImportCancellation\?\.Cancel\(\)/);
assert.doesNotMatch(importFlow.slice(0, importFlow.indexOf('private async Task<int> ReconcilePendingRepostsAsync')),
    /\.reInsert\(|SubmitAsync\(|ConfirmExactRegistrationDialogAsync\(/);
console.log('PASS always-visible A/B/C owned-list import progress, cancellation and safe result reporting');
