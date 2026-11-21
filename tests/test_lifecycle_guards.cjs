'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const app = read('AutomationForm.cs');
const price = read('AutomationForm.SitePriceEdit.cs');
const id = read('ListingIdentity.cs');
const from = (source, start, end) => {
  const a=source.indexOf(start), b=source.indexOf(end,a+start.length);
  assert(a>=0 && b>a, 'missing code region: '+start);
  return source.slice(a,b);
};
const planner = from(price,'private static bool IsUnsubmittedNewListing(', '// Immediate, explicit one-listing operation.');
assert.match(planner, /job\.ImportedFromSite \|\|[\s\S]*job\.Operation == "구매등록" && job\.InitialCompleted && job\.LastSucceededUtc\.HasValue/);
assert.match(planner, /job\.OriginalTradeSide == "구매"/);
assert.match(planner, /ListingIdentity\.HasNumber\(job\.ListingNumber\)/);
assert.match(planner, /MyListingsImport\.IsOwnedListUrl\(OwnedListUrlFor\(job\)\)/);
assert.match(planner, /!IsRegisteredOwnedBuyPriceTarget\(job\) \|\| job\.RequiresReview/);
assert.match(planner, /IsUnsubmittedNewListing\(job\) \? "" : amountValue\.ToString\(\)/);
const submit = from(price,'private async Task<bool> ApplySitePriceAsync(', 'private sealed class SitePriceReadback');
assert.match(submit, /!IsRegisteredOwnedBuyPriceTarget\(job\)/);
assert.match(submit, /ItemManiaAccountStore\.Load\(_system\)\.UserId/);
assert.match(submit, /!string\.Equals\(job\.SiteId, activeAccount, StringComparison\.OrdinalIgnoreCase\)/);
assert.match(submit, /LocateOwnedListRepostAsync\(job, token, requireRepostButton: false\)/);
assert.match(submit, /records\.Count != 1/);
assert.match(submit, /HourlyServerQuota\.MarkAttempt/);
assert.match(submit, /job\.RequiresReview = true/);
assert.match(submit, /ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
const copies=from(app,'private void CopySelectedToOtherServers(', 'private void ImportJobsCsv(');
for (const x of ['copy.PendingSitePriceTarget = ""','copy.PendingRepostBeforeStamp = ""','copy.PendingRepostAttemptUtc = null',
 'job.PendingSitePriceTarget = ""','job.PendingRepostBeforeStamp = ""','job.PendingRepostAttemptUtc = null',
 'job.Operation == "재등록" && job.OriginalTradeSide is "구매" or "판매"']) {
 assert(copies.includes(x), 'clone did not reset inherited state: '+x);
}
const start=from(app,'private void StartScheduler(', 'private void PauseScheduler(');
assert.match(start,/pendingCooldown = job\.NextDueUtc > schedulerStartUtc/);
assert.match(start,/오류 \(미제출\)/);
assert.match(start,/사이트 서버 재등록 간격 대기/);
assert.match(start,/if \(!pendingCooldown\) job\.NextDueUtc = schedulerStartUtc/);
const exec=from(app,'private async Task ExecuteJobAsync(', 'private async Task<OwnedListRepostResult> LocateOwnedListFallbackAsync(');
assert.match(exec,/ListingIdentity\.IsConfirmedNew\(identity, job\)/);
assert.match(exec,/string\.Equals\(identity\.Number, preSubmitListingNumber, StringComparison\.Ordinal\)/);
assert.match(exec,/job\.OriginalTradeSide = flow\.Operation == "판매등록" \? "판매" : "구매"/);
assert.match(exec,/job\.RequiresReview = true/);
assert.match(id,/HasNumber\(e\.Number\) && e\.TitlePresent/);
console.log('PASS new BUY price linkage, exact owner verification, copy reset, restart cooldown and new ID guards');
