'use strict';
// A single user authorization may apply prices to checked BUY listing IDs,
// but the automatic site button must never run unattended on the scheduler
// or proceed past an uncertain result.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8')
  .replace(/\r\n/g, '\n');
const app = read('AutomationForm.SitePriceEdit.cs');
const ui = read('AutomationForm.cs');
const main = read('MainForm.cs');
const site = read('SitePriceEdit.cs');

const start = app.indexOf('private async Task ApplyCheckedSitePricesNowAsync()');
const end = app.indexOf('private async Task<bool> ApplySitePriceAsync(', start);
assert(start > 0 && end > start);
const batch = app.slice(start, end);
assert.match(batch, /_filteredJobs\.Where\(j => j\.SelectedForBulk\)/);
assert.match(batch, /!IsRegisteredOwnedBuyPriceTarget\(j\)/);
assert.match(batch, /!string\.Equals\(j\.SiteId, currentAccount/);
assert.match(batch, /targets\.GroupBy\(j =>/);
assert.match(batch, /MessageBox\.Show\(this,[\s\S]*?선택 구매글 사이트 가격 일괄 수정/);
assert.match(batch, /_sitePriceBatchRunning = true/);
assert.match(batch, /_sitePriceBatchCancellation = new CancellationTokenSource/);
assert.match(batch, /while \(!CanRunNow\(job, out var pacingReason\)\)/);
assert.match(batch, /if \(!await ApplySitePriceAsync\(job, bulkApproval: true, nativeSingleApproval: true,/);
assert.match(batch, /manuallyApprovedPrice: desired, backgroundApproved: true/);
assert.match(batch, /stopReason = job\.ListingNumber/);
assert.match(batch, /break;[\s\S]*?verified\+\+/);
assert.match(batch, /_sitePriceBatchRunning = false/);
assert.doesNotMatch(batch, /ShowVisibleSinglePriceEditor|DispatchVerifiedSitePriceClickAsync|form\.submit/);

assert.match(ui, /FooterButton\("선택 가격 일괄 반영"/);
assert.match(main, /체크한 구매글 가격 일괄 반영/);
assert.match(app, /public Task ApplyCheckedSitePricesFromHeaderAsync\(\)/);
assert.match(app, /backgroundApproved: true\);/);
assert.match(app, /if \(nativeSingleApproval && !backgroundApproved\)/);
assert.match(app, /_unattendedSitePriceEdit = true;\s*await DispatchVerifiedSitePriceClickAsync/);
assert.match(app, /_unattendedSitePriceEdit = false/);
assert.match(app, /if \(_unattendedSitePriceEdit\)[\s\S]*?카테고리 변경 추가 승인을 요청하여 무인 가격 수정을 중단/);
assert.match(app, /job\.RequiresReview = true/);
assert.match(app, /using var submissionObserver = new SitePriceSubmitObserver/);
assert.match(app, /ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
assert.match(site, /AutomaticPriceSubmissionEnabled => true/,
  'Automatic scheduling is available for explicitly authorized price reservations');
console.log('PASS exact-ID headless batch price action, one approval, pacing, site confirmation and stop-on-uncertainty');
