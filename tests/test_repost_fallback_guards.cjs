// Regression checks for the stale-detail -> exact-owned-row fallback and
// numeric listing-ID boundaries. All fixtures are offline and read-only.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourceRoot = path.join(__dirname, '..', 'AppSource');
const read = file => fs.readFileSync(path.join(sourceRoot, file), 'utf8');
const automation = read('AutomationForm.cs');
const imports = read('MyListingsImport.cs');
const bulk = read('AutomationForm.BulkImportApproval.cs');
const models = read('AutomationModels.cs');
const myListingsUi = read('AutomationForm.MyListings.cs');

// Require fallback for BOTH manual dry-run and real submission, and no click
// until the same row is located again at the point of submission.
assert.match(automation, /\[시험\][\s\S]*?LocateOwnedListFallbackAsync\(job, token\)/);
assert.match(automation, /var beforeRepost = await InspectCurrentIdentityAsync\(job, token\);[\s\S]*?LocateOwnedListFallbackAsync\(job, token\)/);
assert.match(automation, /if \(fallback\.Status != "ready"\)[\s\S]*?repostFromOwnedList = true/);
assert.match(automation, /if \(step\.Action == "제출" && repostFromOwnedList\)[\s\S]*?InspectOwnedListRepostAsync\(job, true, token\)/);
assert.match(automation, /job\.RequiresReview = true;[\s\S]*?submitted = true;/);
// Hidden browser must handle the site's native confirm; unknown prompts never
// get blanket approval. Server-rendered time can prove a real AJAX repost.
assert.match(automation, /ScriptDialogOpening \+= OnItemManiaScriptDialog/);
assert.match(automation, /_armedOwnListRepost = job\.ListingNumber/);
assert.match(automation, /e\.Kind == CoreWebView2ScriptDialogKind\.Confirm && !isUnsafe/);
// A native confirm from the owned list is allowed only in its armed exact-ID
// context; a later positive alert from another OFFICIAL site path can close.
assert.match(automation, /MyListingsImport\.IsOwnedListUrl\(currentPage\)/);
assert.match(automation, /officialDialog && e\.Kind == CoreWebView2ScriptDialogKind\.Alert/);
assert.match(automation, /!officialDialog \|\| \(!ownListContext && !officialRepostAlert\)/);
assert.match(automation, /job\.PendingRepostBeforeStamp = flow\.Operation == "재등록" \? preRepostStamp/);
assert.match(automation, /job\.PendingRepostBeforeStamp = "";[\s\S]*?job\.PendingRepostAttemptUtc = null;/);
assert.match(myListingsUi, /ReconcilePendingRepostsAsync\(cancellation\.Token\)/);
assert.match(myListingsUi, /row\.Status != "ready" \|\| string\.IsNullOrWhiteSpace\(row\.Stamp\)/);
assert.match(myListingsUi, /string\.Equals\(row\.Stamp, job\.PendingRepostBeforeStamp, StringComparison\.Ordinal\)/);
assert.match(myListingsUi, /job\.RequiresReview = false;[\s\S]*?job\.LastStatus = "재등록 시각 변경 확인/);
assert.match(models, /CharacterDisplay => string\.IsNullOrWhiteSpace\(Character\)/);
assert.match(myListingsUi, /_config\.DetailEnrichmentCursor = \(start \+ batchSize\) % needing\.Count/);
assert.match(automation, /NavigateAsync\(beforeOwnListUrl, token\)/);
assert.match(automation, /row\.Status == "found" && !string\.IsNullOrWhiteSpace\(row\.Stamp\)/);
assert.match(automation, /if \(!_submissionVerificationPending\) _currentRun\?\.Cancel\(\)/);
assert.match(automation, /nextListRecheck = DateTime\.UtcNow\.AddSeconds\(4\)/);
assert.match(automation, /result\.Method/);
assert.match(automation, /Math\.Clamp\(_config\.MaxSubmissionsPerHour, 1, 1000\)/);
// Known own listing IDs must enter the authenticated management list DIRECTLY;
// a stale detail URL is a last resort only for legacy unknown trade direction.
const entryChoice = automation.match(/private static string EntryFor\(ListingJob job, AutomationWorkflow flow\) =>([\s\S]*?);/)?.[1] || '';
assert(entryChoice.indexOf('OwnedListUrlFor(job)') >= 0);
assert(entryChoice.indexOf('OwnedListUrlFor(job)') < entryChoice.indexOf('IsSafeDetailUrl(job.EntryUrl'));
assert.match(bulk, /IsSafeDetailUrl\(j\.EntryUrl, j\.ListingNumber\) \|\|[\s\S]*?OwnedListUrlFor\(j\)/);
assert.match(models, /loaded\.MinimumServerSeconds = Math\.Clamp\(loaded\.MinimumServerSeconds, 21, 3600\)/);
assert.match(imports, /Regex\.Escape\(number!\).*?RegexOptions\.CultureInvariant/s);

const scriptStart = imports.indexOf('public const string ExtractDetailScript');
const quoteStart = imports.indexOf('"""', scriptStart);
const quoteEnd = imports.indexOf('"""', quoteStart + 3);
assert.ok(scriptStart >= 0 && quoteStart >= 0 && quoteEnd > quoteStart);
const script = imports.slice(quoteStart + 3, quoteEnd)
  .replace('__EXPECTED_NUMBER__', '"2026070507571234"');
function readDetail(url) {
  const u = new URL(url);
  const context = {
    URL,
    location: { href: url, protocol: u.protocol, hostname: u.hostname, pathname: u.pathname, search: u.search },
    document: { querySelectorAll: () => [] }
  };
  return vm.runInNewContext(script, context);
}
assert.equal(readDetail('https://trade.itemmania.com/item/detail?tid=20260705075712345').listingNumber, '');
assert.equal(readDetail('https://trade.itemmania.com/item/detail?tid=2026070507571234').listingNumber, '2026070507571234');
console.log('PASS stale detail fallback, zero-post safety, batch approval, pacing and exact-ID read');
