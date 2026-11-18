'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');

const detail = read('AutomationForm.cs');
const batch = read('AutomationForm.BatchEditorModern.cs');
const integrated = read('AutomationForm.IntegratedEditing.cs');
const main = read('MainForm.IntegratedEditor.cs');
const undo = read('AutomationForm.BatchUndo.cs');
const model = read('AutomationModels.cs');

// All title-editing paths protect live registered posts; newly drafted posts stay editable.
assert.match(detail, /ListingIdentity\.HasNumber\(_selected\.ListingNumber\)/);
assert.match(detail, /!string\.Equals\(proposedTitle, _selected\.Title, StringComparison\.Ordinal\)/);
assert.match(batch, /field == "물품 제목" && active\.Any\(j => ListingIdentity\.HasNumber\(j\.ListingNumber\)\)/);
assert.match(integrated, /command\.Field == IntegratedEditField\.Title &&/);

// Local detail changes invalidate stale approved price reservations immediately.
assert.match(detail, /_selected\.PendingSitePriceTarget = "";/);
assert.match(detail, /기존 사이트 가격 예약/);
assert.match(model, /가격 \{target\}원 · 다음 순번/);
assert.match(batch, /newPendingPrice: siteChoice == DialogResult\.Yes \? price\.ToString\(\) : ""/);

// Undo is a one-step, local-only receipt for both the single-system and ABC editors.
assert.match(batch, /dialogUndo = RememberBatchUndo\(field, changes\)/);
assert.match(batch, /TryUndoBatchEdit\(dialogUndo, out issue\)/);
assert.match(detail, /selectionTools\.Items\.Add\("마지막 일괄수정 되돌리기"/);
assert.match(integrated, /RememberBatchUndo\(command\.Field\.ToString\(\), undoChanges\)/);
assert.match(main, /_integratedLastUndo = receipts/);
assert.match(main, /UndoIntegratedBatchEdit\(\)/);

// Never restore if the site has successfully submitted since the local edit.
assert.match(undo, /change\.Job\.LastSucceededUtc != change\.SuccessAtApplication/);
assert.match(undo, /change\.MatchesBefore\(\) : !change\.MatchesAfter\(\)/);
assert.match(undo, /_deferredExecutingJobEdits\.Remove\(change\.DeferredAction!\)/);
assert.match(undo, /if \(newPendingPrice is not null\) job\.PendingSitePriceTarget =/);
assert.doesNotMatch(undo, /CoreWebView2|ExecuteStepAsync|Navigate\(/);
console.log('PASS title safety across editors, immediate price reservation cancellation and local-only batch undo guards');
