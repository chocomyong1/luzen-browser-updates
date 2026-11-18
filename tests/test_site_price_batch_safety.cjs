'use strict';

// Offline source guard: never sends anything to a live account or web endpoint.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const price = read('AutomationForm.SitePriceEdit.cs');
const editor = read('AutomationForm.BatchEditorModern.cs');
const form = read('AutomationForm.cs');

const batch = price.slice(price.indexOf('private async Task ApplySelectedSitePricesAsync('),
  price.indexOf('private async Task<bool> ApplySitePriceAsync('));
assert.doesNotMatch(batch, /일괄 가격 변경 일시 중단|#pragma warning disable CS0162/,
  'Batch must not be permanently disabled');
assert.match(batch, /if \(targets\.Count > 20\)/);
assert.match(batch, /!j\.ImportedFromSite \|\| j\.OriginalTradeSide != "구매" \|\| j\.RequiresReview/);
assert.match(batch, /!ListingIdentity\.HasNumber\(j\.ListingNumber\)/);
assert.match(batch, /MyListingsImport\.IsOwnedListUrl\(OwnedListUrlFor\(j\)\)/);
assert.match(batch, /duplicateIds/);
assert.match(batch, /accountMismatch \|\| mixedAccounts/);
assert.match(batch, /MessageBoxButtons\.YesNo/);
assert.match(batch, /foreach \(var item in snapshots\)/);
assert.match(batch, /if \(!await WaitForSitePriceBatchSpacingAsync\(job, cancellation\.Token\)\)/);
assert.match(batch, /if \(!await ApplySitePriceAsync\(job, true\)\)/);
assert.match(batch, /break;/);
assert.match(batch, /_sitePriceBatchRunning = true/);
assert.match(batch, /_sitePriceBatchCancellation = cancellation/);
assert.match(batch, /catch \(OperationCanceledException\)/);
assert.match(batch, /return CanRunNow\(job, out _\)/);
assert.match(batch, /HourlyServerQuota\.NextAllowedUtc/);
assert.match(batch, /HourlyServerQuota\.Remaining/);
assert.doesNotMatch(batch, /Task\.WhenAll|Parallel\.ForEach|Task\.Run/,
  'Site submissions must remain sequential');
assert.match(price, /if \(_sitePriceBatchRunning && !bulkApproval\)/);
assert.match(form, /_sitePriceBatchCancellation\?\.Cancel\(\)/);
assert.match(form, /private void SelectSamePriceListings\(\)/);
assert.match(form, /var batch = matched\.Take\(20\)\.ToList\(\)/);
assert.match(form, /job\.OriginalTradeSide == "구매"/);
assert.match(form, /job\.SiteId, reference\.SiteId/);
assert.match(editor, /siteApply = new Button \{ Text = "사이트에 순차 반영"/);
assert.match(editor, /Enabled = false/);
assert.match(editor, /changedPriceTargets = activePriceTargets/);
assert.match(editor, /MessageBoxButtons\.YesNoCancel/);
assert.match(editor, /sitePriceConsentConfirmed = siteChoice == DialogResult\.Yes/);
assert.match(editor, /if \(sitePriceConsentConfirmed\)[\s\S]*?window\.DialogResult = DialogResult\.OK/);
assert.match(editor, /ApplySelectedSitePricesAsync\(changedPriceTargets, sitePriceConsentConfirmed\)/);
assert.match(price, /\(!sitePriceConsentConfirmed \|\| explicitTargets is null\)/,
  'A site submit must require explicit user approval in editor or batch preview');
console.log('PASS same-price selection and serialized verified site price batch safety');
