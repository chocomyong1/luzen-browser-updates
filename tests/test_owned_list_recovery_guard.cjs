'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');
const runner = source('AutomationForm.cs');
const owned = source('AutomationForm.MyListings.cs');
const split = source('AutomationForm.SplitStatus.cs');

// Empty-looking pages are NOT proof that a listing is missing. The explicit
// unrendered-list error must become a system-level pre-submit cooldown.
assert.match(runner, /result\.Message\.Contains\("관리 목록의 물품 행이 끝내 표시되지 않았습니다"/);
const locate = runner.split('private async Task<OwnedListRepostResult> LocateOwnedListRepostAsync(')[1]
  ?.split('private static bool IsTransientOwnedListPageError(')[0] || '';
assert.match(locate, /if \(IsTransientOwnedListPageError\(row\)\)/);
assert.match(locate, /throw new OwnedListConnectivityPendingException/);
assert.match(locate, /if \(row\.Message\.Contains\("관리 목록에 로그인해야 합니다"/);
assert.match(locate, /throw new OwnedListLoginPendingException/);
const pendingA = runner.split('catch (OwnedListConnectivityPendingException ex) when (!submitted && _system != "B")')[1]
  ?.split('catch (OwnedListConnectivityPendingException ex) when (!submitted && _system == "B")')[0] || '';
assert.match(pendingA, /_ownedListRecoveryUtc = DateTime\.UtcNow\.AddSeconds\(90\)/);
assert.match(pendingA, /job\.NextDueUtc = _ownedListRecoveryUtc/);
assert.match(pendingA, /관리목록 표시 복구 대기 \(미제출\)/);
assert.doesNotMatch(pendingA, /RequiresReview = true|MarkAttempt|\.click\(|ExecuteStepAsync/);

const scheduler = runner.split('private async Task SchedulerTickAsync()')[1]
  ?.split('private async Task VerifyOneHeldListingInBackgroundAsync(')[0] || '';
assert.match(scheduler, /_ownedListRecoveryUtc != DateTime\.MinValue/);
assert.match(scheduler, /_ownedListRecoveryUtc = DateTime\.UtcNow\.AddMinutes\(2\)/);
assert.match(scheduler, /if \(await VerifyOwnedListReadableAsync\(check\.Token\) && _schedulerRunning\)/);
const probe = scheduler.split('private async Task<bool> VerifyOwnedListReadableAsync(')[1] || '';
assert.match(probe, /MyListingsImport\.SellListingsUrl : MyListingsImport\.BuyListingsUrl/);
assert.match(probe, /NavigateOwnedListAsync\(url, token\)/);
assert.match(probe, /MyListingsImport\.DecodePage\(raw\)/);
assert.match(probe, /page\.IsEmptyList/);
assert.match(probe, /저장된 등록번호를 확인할 수 없어 자동 재개하지 않습니다/);
assert.match(probe, /page\.Listings\.Count > 0/);
assert.match(probe, /if \(page\.LoginRequired\)/);
assert.doesNotMatch(probe, /\.click\(|ExecuteStepAsync|MarkAttempt|\.Submit\(/);
assert(scheduler.indexOf('_ownedListRecoveryUtc != DateTime.MinValue') < scheduler.indexOf('await ExecuteJobAsync(job, false)'),
  'A/C must check recovery before running the next job');
assert.match(runner, /_ownedListRecoveryRun\?\.Cancel\(\)/);
assert.match(owned, /if \(expectOwnedList && sawTransportFailure\)/);
assert.match(runner, /MissingAfterCompleteScan = firstPageRechecked && queue\.Count == 0 && loadedPages > 0 &&\s*visited\.Count < maxPages && unrenderedPages == 0 && numberRows == 0/);
assert.match(runner, /if \(missingOwnedListing && !submitted\)\s*\{\s*job\.Enabled = false;\s*job\.NextDueUtc = DateTime\.MaxValue;/);
assert.match(split, /관리목록 복구 대기/);
console.log('PASS system-wide unrendered-list cooldown, B VPN + DOM probe, manual stop and zero-submit recovery');
