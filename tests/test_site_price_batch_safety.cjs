'use strict';

// Offline source guard: no account, browser navigation, or live site requests.
// Approved price changes must join the existing hourly paced job scheduler.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const price = read('AutomationForm.SitePriceEdit.cs');
const editor = read('AutomationForm.BatchEditorModern.cs');
const form = read('AutomationForm.cs');
const models = read('AutomationModels.cs');
const importCode = read('MyListingsImport.cs');

const reservation = price.slice(price.indexOf('private async Task ApplySelectedSitePricesAsync('),
  price.indexOf('private async Task<bool> ApplySitePriceAsync('));
assert.match(reservation, /if \(targets\.Count > 20\)/);
assert.match(reservation, /!j\.ImportedFromSite \|\| j\.OriginalTradeSide != "구매" \|\| j\.RequiresReview/);
assert.match(reservation, /!ListingIdentity\.HasNumber\(j\.ListingNumber\)/);
assert.match(reservation, /MyListingsImport\.IsOwnedListUrl\(OwnedListUrlFor\(j\)\)/);
assert.match(reservation, /duplicateIds/);
assert.match(reservation, /accountMismatch \|\| mixedAccounts/);
assert.match(reservation, /MessageBoxButtons\.YesNo/);
assert.match(reservation, /job\.PendingSitePriceTarget = job\.Price/);
assert.match(reservation, /_config\.Save\(\)/);
assert.doesNotMatch(reservation, /PauseScheduler\(\)|ApplySitePriceAsync\(job|_sitePriceBatchRunning = true|NavigateOwnedListAsync|ExecuteScriptAsync|WaitForSitePriceBatchSpacingAsync/,
  'Queuing prices must never pause, consume a slot, or submit to the site');

const tick = form.slice(form.indexOf('private async Task SchedulerTickAsync()'),
  form.indexOf('private async Task ExecuteJobAsync(', form.indexOf('private async Task SchedulerTickAsync()')));
assert.match(tick, /if \(!CanRunNow\(job, out _\)\) continue;/);
assert.match(tick, /if \(!string\.IsNullOrWhiteSpace\(job\.PendingSitePriceTarget\)\)/);
assert.match(tick, /reserved != local/);
assert.match(tick, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true\)/);
assert.match(tick, /job\.PendingSitePriceTarget = ""/);
assert.match(tick, /job\.RequiresReview = true;[\s\S]*?job\.Enabled = false/);
assert.match(tick, /await ExecuteJobAsync\(job, false\)/);

const submit = price.slice(price.indexOf('private async Task<bool> ApplySitePriceAsync('));
assert.match(submit, /if \(_schedulerRunning && !fromScheduler\) PauseScheduler\(\)/);
assert.match(submit, /if \(fromScheduler && !_schedulerRunning\)/);
assert.match(submit, /using var submissionObserver = new SitePriceSubmitObserver/);
assert.match(submit, /HourlyServerQuota\.MarkAttempt/);
assert.match(submit, /job\.RequiresReview = true/);

assert.match(models, /public string PendingSitePriceTarget \{ get; set; \} = "";/);
assert.match(editor, /x\.PendingSitePriceTarget = siteChoice == DialogResult\.Yes \? v : ""/);
assert.match(editor, /_deferredExecutingJobEdits\.Add\(\(\) => write\(item, value\)\)/);
assert.match(editor, /Text = "다음 순번에 예약"/);
assert.match(editor, /MessageBoxButtons\.YesNoCancel/);
assert.match(importCode, /string\.IsNullOrWhiteSpace\(Target\.PendingSitePriceTarget\)/);
assert.match(importCode, /sameOwnedListing && string\.IsNullOrWhiteSpace\(target\.PendingSitePriceTarget\)/);
console.log('PASS safe deferred site price changes on ordinary repost slots');
