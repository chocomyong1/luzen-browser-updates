'use strict';
// Verify uncertain one-off price submissions are reconciled read-only before
// the user may explicitly authorize a fresh single-listing attempt.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../AppSource/AutomationForm.SitePriceEdit.cs'),'utf8');
const action=source.slice(source.indexOf('private async Task ApplySelectedSitePriceAsync('),
  source.indexOf('// Never edits a field or submits a listing.'));
const reconcile=action.slice(action.indexOf('private async Task<bool> ReconcileHeldPriceBeforeImmediateEditAsync('));
assert.match(action,/if \(job\.RequiresReview\)[\s\S]*?ReconcileHeldPriceBeforeImmediateEditAsync\(job\)/);
assert.match(reconcile,/job\.LastStatus\.Contains\("가격"/);
assert.match(reconcile,/TrySitePrice\(job\.PendingSitePriceTarget, out var attemptedTarget\)/);
assert.match(reconcile,/ItemManiaAccountStore\.Load\(_system\)\.UserId/);
assert.match(reconcile,/ReadSitePriceAfterRepostAsync\(job, attemptedTarget, cancellation\.Token\)/);
assert.match(reconcile,/if \(readback\.ExactIdVerified\)/);
assert.match(reconcile,/job\.PendingSitePriceTarget = ""/);
assert.match(reconcile,/if \(!readback\.OriginalUnchangedNoReplacement\)/);
assert.match(reconcile,/MessageBoxDefaultButton\.Button2\) != DialogResult\.Yes/);
assert.match(reconcile,/job\.Enabled = false/);
assert.doesNotMatch(reconcile,/DispatchVerifiedSitePriceClickAsync|ApplySitePriceAsync|ExecuteJobAsync|form\.submit\(/);
const reader=source.slice(source.indexOf('private async Task<SitePriceReadback> ReadSitePriceAfterRepostAsync('),
  source.indexOf('private void HandleSitePriceEditScriptDialog('));
assert.match(reader,/currentOld == previouslyVerifiedOld && currentOld != target/);
assert.match(reader,/!legacyTitleFallback && expectedTitle\.Length > 0/);
assert.match(reader,/newCandidates\.Count == 0 && queue\.Count == 0 && visited\.Count < maxPages/);
assert.match(reader,/PriceReadbackTitle\(matchingOld\[0\]\.Title\) == expectedTitle/);
assert.match(reader,/OriginalUnchangedNoReplacement = originalUnchangedNoReplacement/);
console.log('PASS held price reconciliation is read-only, strict, and manually confirmed before another submit');
