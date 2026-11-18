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
const importReview = read('AutomationForm.ImportReview.cs');

// Require fallback for BOTH manual dry-run and real submission, and no click
// until the same row is located again at the point of submission.
assert.match(automation, /\[시험\][\s\S]*?LocateOwnedListFallbackAsync\(job, token\)/);
assert.match(automation, /var beforeRepost = await InspectCurrentIdentityAsync\(job, token\);[\s\S]*?LocateOwnedListFallbackAsync\(job, token\)/);
assert.match(automation, /if \(fallback\.Status != "ready"\)[\s\S]*?repostFromOwnedList = true/);
assert.match(automation, /if \(step\.Action == "제출" && repostFromOwnedList\)[\s\S]*?InspectOwnedListRepostAsync\(job, true, token\)/);
// A second program may move the target to the newest page while we paginate.
// Recheck the current first page READ-ONLY, then still require the exact row.
const fallbackSearch = automation.split('private async Task<OwnedListRepostResult> LocateOwnedListRepostAsync(')[1]?.split('private async Task<OwnedListRepostResult> InspectOwnedListRepostAsync(')[0] || '';
assert.match(fallbackSearch, /await NavigateOwnedListAsync\(baseUrl, token\);[\s\S]*?InspectOwnedListPageAsync\(job, token, requireRepostButton\)/);
assert.match(fallbackSearch, /await NavigateOwnedListAsync\(next, token\)/);
assert.match(fallbackSearch, /page\.Listings\.Count > 0 \|\| page\.IsEmptyList/);
assert.match(fallbackSearch, /pageNotReady = page\.Listings\.Count == 0 && !page\.IsEmptyList/);
assert.match(fallbackSearch, /등록번호는 확인했지만 저장된 제목이 현재 사이트와 다릅니다/);
assert.match(fallbackSearch, /await Task\.Delay\(750, token\)/);
assert.match(fallbackSearch, /if \(updated\.Status == "ready" \|\| \(!requireRepostButton && updated\.Status == "found"\)\)/);
assert.doesNotMatch(fallbackSearch, /InspectOwnedListRepostAsync\(job, true, token\)/);
assert.match(fallbackSearch, /_jobs\.Select\(j => j\.SourceListUrl\)/);
assert.match(fallbackSearch, /MyListingsImport\.IsOwnedListUrl\(u\)/);
assert.match(fallbackSearch, /MyListingsImport\.IsSellListUrl\(u\) == selling/);
assert.match(fallbackSearch, /foreach \(string knownPage in knownPages\) queue\.Enqueue\(knownPage\)/);
assert.match(automation, /job\.RequiresReview = true;[\s\S]*?submitted = true;/);
// Hidden browser must handle the site's native confirm; unknown prompts never
// get blanket approval. Server-rendered time can prove a real AJAX repost.
assert.match(automation, /ScriptDialogOpening \+= OnItemManiaScriptDialog/);
assert.match(automation, /_armedOwnListRepost = job\.ListingNumber/);
assert.match(automation, /e\.Kind == CoreWebView2ScriptDialogKind\.Confirm && !isUnsafe/);
assert.match(automation, /compactMessage\.Contains\("해당서버에아직등록하실수없습니다"/);
const cooldownRecovery = automation.split('catch (Exception ex) when (submitted && _repostServerCooldownRejected)')[1]
  ?.split('catch (Exception ex)')[0] || '';
assert.match(cooldownRecovery, /job\.RequiresReview = false/);
assert.match(cooldownRecovery, /job\.PendingRepostAttemptUtc = null/);
assert.match(cooldownRecovery, /Math\.Max\(60, _config\.MinimumServerSeconds\)/);
assert.doesNotMatch(cooldownRecovery, /PauseScheduler\(/);
const uncertainSubmissionRecovery = automation.split('catch (Exception ex) when (submitted && _repostServerCooldownRejected)')[1]
  ?.split('catch (Exception ex)')[1]?.split('finally')[0] || '';
assert.doesNotMatch(uncertainSubmissionRecovery, /PauseScheduler\(/);
assert.match(uncertainSubmissionRecovery, /해당 물품은 사이트의 정확한 등록번호·재등록 시각을 백그라운드에서 검사합니다/);
assert.match(uncertainSubmissionRecovery, /job\.RequiresReview = true/);
assert.match(uncertainSubmissionRecovery, /submitted \? DateTime\.MaxValue/);
// A native confirm from the owned list is allowed only in its armed exact-ID
// context; a later positive alert from another OFFICIAL site path can close.
assert.match(automation, /MyListingsImport\.IsOwnedListUrl\(currentPage\)/);
assert.match(automation, /officialDialog && e\.Kind == CoreWebView2ScriptDialogKind\.Alert/);
assert.match(automation, /!officialDialog \|\| \(!ownListContext && !officialRepostAlert && !officialCooldownAlert\)/);
assert.match(automation, /job\.PendingRepostBeforeStamp = flow\.Operation == "재등록" \? preRepostStamp/);
// Reposting a verified existing ID does not create a new ID. After the exact
// owned row was clicked and the site independently confirmed success, keep
// the original ID even if the result page no longer displays that ID.
assert.match(automation, /if \(result\.Status != "clicked"\)[\s\S]*?exactOwnedRepostSubmitted = true/);
assert.match(automation, /bool verified = await VerifySuccessAsync\([\s\S]*?if \(!verified\) throw/);
assert.match(automation, /bool exactOwnedRepostConfirmed = flow\.Operation == "재등록" &&[\s\S]*?repostFromOwnedList && exactOwnedRepostSubmitted &&[\s\S]*?ListingIdentity\.HasNumber\(job\.ListingNumber\) &&[\s\S]*?MyListingsImport\.IsOwnedListUrl\(preRepostListUrl\)/);
assert.match(automation, /identityOk = flow\.Operation == "재등록" \?[\s\S]*?ListingIdentity\.IsConfirmedExisting\(identity, job\) :[\s\S]*?ListingIdentity\.IsConfirmedNew\(identity, job\)/);
assert.match(automation, /job\.PendingRepostBeforeStamp = "";[\s\S]*?job\.PendingRepostAttemptUtc = null;/);
assert.match(myListingsUi, /ReconcilePendingRepostsAsync\(cancellation\.Token\)/);
// Uncertain items are reconciled in the background, one at a time, using
// an exact ID/title match AND a changed server-side stamp. The method must
// never assume an old (<2h) pending checkpoint is safe to repost.
assert.match(automation, /await VerifyOneHeldListingInBackgroundAsync\(candidate\)/);
const schedulerTick = automation.split('private async Task SchedulerTickAsync()')[1]
  ?.split('private async Task VerifyOneHeldListingInBackgroundAsync')[0] || '';
assert(schedulerTick.indexOf('await ExecuteJobAsync(job, false)') >= 0);
assert(schedulerTick.indexOf('await ExecuteJobAsync(job, false)') <
       schedulerTick.indexOf('await VerifyOneHeldListingInBackgroundAsync(candidate)'),
  'do not delay a ready normal listing to inspect pending submissions');
assert.match(automation, /_nextPendingReviewUtc = DateTime\.UtcNow\.AddMinutes\(2\)/);
assert.match(automation, /if \(_busy \|\| !_schedulerRunning \|\| _myListingsImportRunning \|\| _pendingMyListingsImport\) return/);
assert.match(automation, /await ReconcilePendingRepostsAsync\(cancellation\.Token, candidate\)/);
assert.match(automation, /requireRepostButton \? false : null/);
assert.match(automation, /_pendingReviewRun\?\.Cancel\(\)/);
assert.match(automation, /Math\.Max\(60, _config\.MinimumServerSeconds\)/);
const pendingReconcile = myListingsUi.split('private async Task<int> ReconcilePendingRepostsAsync(')[1]
  ?.split('private sealed class MyListingsScan')[0] || '';
assert.match(pendingReconcile, /ReferenceEquals\(j, onlyJob\)/);
assert.doesNotMatch(pendingReconcile, /AddHours\(-2\)/);
assert.match(pendingReconcile, /requireRepostButton: false/);
assert.doesNotMatch(pendingReconcile, /InspectOwnedListRepostAsync\(job, true, token\)/);
assert.match(myListingsUi, /row\.Status != "found" \|\| string\.IsNullOrWhiteSpace\(row\.Stamp\)/);
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
assert.match(models, /!Enabled \? "자동 꺼짐"/);
assert.match(models, /j\.Enabled && !j\.RequiresReview &&[\s\S]*?j\.NextDueUtc == DateTime\.MaxValue/);
assert.match(imports, /Target\.Price == "1" && !string\.IsNullOrWhiteSpace\(Price\)/);
assert.match(imports, /const viewLabel=matching\.filter/);
assert.match(imports, /Regex\.Escape\(number!\).*?RegexOptions\.CultureInvariant/s);
// Fresh account imports may lack the old detail URL without being blocked.
// A potentially submitted listing is NEVER released by the import refresh.
const trustCheck = imports.match(/private static bool CanTrustOwnedImport\([\s\S]*?;\r?\n/)?.[0] || '';
assert.match(trustCheck, /IsOwnedListUrl\(row\.SourceListUrl\)/);
assert.match(trustCheck, /row\.TradeSide == "판매"/);
assert.match(trustCheck, /row\.TradeSide == "구매"/);
assert.doesNotMatch(trustCheck, /IsSafeDetailUrl/);
assert.match(imports, /!target\.PendingRepostAttemptUtc\.HasValue[\s\S]*?IsImportOnlyStatus\(target\.LastStatus\)/);
assert.match(imports, /StartsWith\("내 등록글 정보 갱신됨"/);
// A past ambiguous POST can only be retried after the user confirms they
// checked the exact listing and duplicates; one-off consent stays auto-OFF.
assert.match(importReview, /HasUnverifiedSubmission\(job\)/);
assert.match(importReview, /job\.PendingRepostAttemptUtc\.HasValue/);
assert.match(importReview, /이전 재등록 결과·중복 여부와 해당 등록번호·금액을 사이트에서 확인했습니다/);
assert.match(automation, /if \(!dryRun && !importedReviewConfirmed && MessageBox\.Show/);
assert.match(automation, /if \(importedReviewConfirmed\)[\s\S]*?_selected\.Enabled = false;/);

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
