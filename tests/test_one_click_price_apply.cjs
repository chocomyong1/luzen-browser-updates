'use strict';

// No browser or account access. Verify all three editor paths prepare site
// prices only for the listing's existing paced scheduler turn.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');

const single = read('AutomationForm.cs');
const planner = read('AutomationForm.SitePriceEdit.cs');
const bulk = read('AutomationForm.BatchEditorModern.cs');
const abc = read('AutomationForm.IntegratedEditing.cs');
const abcDialog = read('MainForm.IntegratedEditor.cs');

// User-facing single edit: Save must schedule (or safely reject), without
// requiring any second button or cancelling a prior reservation.
assert.match(single, /TryPlanAutomaticPriceApply\(new\[\] \{ _selected \}, proposedPrice/);
assert.match(single, /item\.Price = proposedPrice;\s*item\.PendingSitePriceTarget = pending/);
assert.match(single, /ReferenceEquals\(item, _currentlyExecutingJob\)/);
assert.match(single, /_deferredExecutingJobEdits\.Add\(\(\) =>/);
assert.doesNotMatch(single, /ActionButton\("가격 반영 예약"|FooterButton\("가격 변경 예약"/);
assert.doesNotMatch(single, /기존 사이트 가격 예약.*취소/);

// Drafts (both BUY and SELL) have no posted number or prior submit; never
// attempt to call the existing-listing price endpoint for their first post.
const draft = planner.slice(planner.indexOf('private static bool IsUnsubmittedNewListing('),
  planner.indexOf('// Immediate, explicit one-listing operation.'));
for (const required of ['!job.ImportedFromSite', '!ListingIdentity.HasNumber(job.ListingNumber)',
  '!job.InitialCompleted', '!job.LastSucceededUtc.HasValue', '!job.PendingRepostAttemptUtc.HasValue',
  'job.Operation is "구매등록" or "판매등록"'])
  assert.ok(draft.includes(required), `Draft predicate missing: ${required}`);
assert.match(draft, /var changed = selected\.Where\(job =>/);
assert.match(draft, /var existing = changed\.Where\(job => !IsUnsubmittedNewListing\(job\)\)/);
assert.doesNotMatch(draft, /existing\.Count\s*>\s*20|최대 20건/);
assert.match(draft, /!IsRegisteredOwnedBuyPriceTarget\(job\) \|\| job\.RequiresReview/);
assert.match(draft, /MyListingsImport\.IsOwnedListUrl\(OwnedListUrlFor\(job\)\)/);
assert.match(draft, /ItemManiaAccountStore\.Load\(_system\)\.UserId/);
assert.match(draft, /_jobs\.Any\(other => !ReferenceEquals\(other, job\)/);
assert.match(draft, /!changed\.Contains\(job\)/);
assert.match(draft, /IsUnsubmittedNewListing\(job\) \? "" : amountValue\.ToString\(\)/);
assert.doesNotMatch(draft, /if \(existing\.Count > 0 && !SitePriceEdit\.AutomaticPriceSubmissionEnabled\)/,
  'A disabled scheduler must not prevent saving prices for explicit checked-item submission');
assert.doesNotMatch(draft, /Navigate\(|ExecuteScriptAsync|ApplySitePriceAsync\(/);

// Bulk + ABC apply both use per-listing pending values, so selecting drafted
// posts together with verified BUY posts doesn't schedule the drafts.
assert.match(bulk, /TryPlanAutomaticPriceApply\(activePriceTargets, price\.ToString\(\)/);
assert.match(bulk, /pendingForJob: job => pendingByJob\[job\]/);
assert.match(bulk, /pendingForJob\?\.Invoke\(item\)/);
assert.doesNotMatch(bulk, /siteChoice|siteApply|MessageBoxButtons\.YesNoCancel/);
assert.match(abc, /TryPlanAutomaticPriceApply\(items, value, out _, out problem\)/);
assert.match(abc, /command\.Field == IntegratedEditField\.Price\s*\? pendingByJob\[job\]/);
assert.match(abc, /Action afterSubmission = \(\) => ApplyBatchUndoChange\(change\)/);
assert.doesNotMatch(abcDialog, /var reserve = new CheckBox|reserveSite/);

// The old scheduler remains guarded and cannot submit prices; explicit
// checked-list action has its own quota-checked native click and readback.
const schedule = single.slice(single.indexOf('private async Task SchedulerTickAsync()'),
  single.indexOf('private async Task ExecuteJobAsync(', single.indexOf('private async Task SchedulerTickAsync()')));
assert.match(schedule, /if \(!CanRunNow\(job, out _\)\) continue;/);
assert.match(schedule, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true\)/);
assert.match(schedule, /job\.RequiresReview = true;/);
assert.match(planner, /ApplyCheckedSitePricesNowAsync\(\)/);
assert.match(planner, /nativeSingleApproval: true,[\s\S]*?backgroundApproved: true/);
assert.match(bulk, /기존 본인 구매글은 저장 후 순차적으로 사이트 가격 수정을 시도합니다/);
console.log('PASS one-click price apply: new posts, verified BUY posts, A/B/C bulk and paced safety');
