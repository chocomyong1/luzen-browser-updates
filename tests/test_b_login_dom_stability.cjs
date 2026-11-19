'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8').replace(/\r\n/g, '\n');
const list = source('AutomationForm.MyListings.cs');
const runner = source('AutomationForm.cs');
const login = source('ItemManiaLogin.cs');
const nav = list.split('private async Task NavigateOwnedListAsync(')[1];
const readiness = list.split('private async Task<OwnedListPageReadiness> WaitForOwnedListPageReadinessAsync(')[1]
  ?.split('private async Task NavigateOwnedListAsync(')[0] || '';

// A URL match is insufficient to resume posting. Require stable real DOM
// evidence twice; wait for the background login navigation to finish.
assert.match(nav, /await WaitForOwnedListPageReadinessAsync\(url, token\)/);
assert.match(nav, /readiness == OwnedListPageReadiness\.Ready/);
assert.match(readiness, /MyListingsImport\.ExtractPageScript/);
assert.match(readiness, /page\.Listings\.Count > 0 \|\| page\.IsEmptyList/);
assert.match(readiness, /\+\+verifiedReads >= 2/);
assert.match(readiness, /_autoLogin\.WaitForLoginNavigationAsync\(token\)/);
assert.match(readiness, /if \(page\.LoginRequired\) return OwnedListPageReadiness\.Redirected/);
assert.doesNotMatch(readiness, /\.click\(|SubmitAsync|Invoke\(/);

// Proven login redirect is not a VPN disconnect. In particular, a website
// changing the displayed path must not trigger a WARP reset.
assert.match(runner, /IsUnexpectedOwnedListPageError\(row\)/);
assert.match(runner, /throw new OwnedListLoginPendingException\(\s*"관리목록 대신 다른 화면/);
assert.match(runner, /_autoLogin\?\.ObserveOwnedListLoginRedirect\(\)/);
assert.match(login, /public void ObserveOwnedListLoginRedirect\(\)/);
assert.match(login, /DateTime\.UtcNow - _lastAttemptUtc >= TimeSpan\.FromMinutes\(2\)/);
assert.match(login, /if \(DateTime\.UtcNow - _lastAttemptUtc < TimeSpan\.FromMinutes\(10\)\) return/);
assert.match(runner, /_bRepairProxyOnRecovery = ex\.TransportFailure/);
assert.match(runner, /if \(_bRepairProxyOnRecovery\)/);
assert.match(runner, /if \(!verifiedBRoute\) _bRepairProxyOnRecovery = true/);
assert.match(list, /transportFailure: true/);
assert.match(runner, /관리목록이 아닌 화면으로 이동했습니다\. 로그인 복구가 필요합니다 \(미제출\)/);

// A and B profiles remain separated; the route is verified before readback.
assert.match(runner, /await DualSystemNetwork\.VerifyBRouteAsync\(check\.Token\)/);
assert.match(runner, /await VerifyOwnedListReadableAsync\(check\.Token\)/);
assert(runner.indexOf('await DualSystemNetwork.VerifyBRouteAsync(check.Token)') <
  runner.indexOf('await VerifyOwnedListReadableAsync(check.Token)'));
console.log('PASS login redirect detection, stable owned-list DOM and conditional B proxy repair');
