// Offline source guard: a background login navigation must not be mistaken
// for an owned-list navigation (and transient retries must remain read-only).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const src = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const automation = src('AutomationForm.cs');
const owned = src('AutomationForm.MyListings.cs');
const login = src('ItemManiaLogin.cs');

const moveStep = automation.split('if (step.Action == "이동")')[1]?.split('if (string.IsNullOrWhiteSpace(step.Selector))')[0] || '';
assert.match(moveStep, /MyListingsImport\.IsOwnedListUrl\(url\)/);
assert.match(moveStep, /if \(!alreadyHere\) await NavigateOwnedListAsync\(url, token\)/);

const genericNav = automation.split('private async Task NavigateAsync(')[1]?.split('private async Task<bool> WaitForElementAsync(')[0] || '';
assert.match(genericNav, /NavigationStarting \+= starting/);
assert.match(genericNav, /NavigationCompleted \+= completed/);
assert.match(genericNav, /result\.NavigationId == requestedNavigationId\.Value/);
assert.match(genericNav, /NavigationStarting -= starting/);
assert.match(genericNav, /NavigationCompleted -= completed/);
assert.doesNotMatch(genericNav, /TrySetResult\(result\.IsSuccess\)/);

const managedNav = owned.split('private async Task NavigateOwnedListAsync(')[1] || '';
assert.match(managedNav, /const int maxAttempts = 3/);
assert.match(managedNav, /e\.NavigationId == targetId\.Value/);
assert.match(managedNav, /expectOwnedList && MyListingsImport\.IsOwnedListUrl\(arrived\)/);
assert.match(managedNav, /MyListingsImport\.IsSellListUrl\(arrived\) == MyListingsImport\.IsSellListUrl\(url\)/);
assert.match(managedNav, /await Task\.Delay\(attempt == 1 \? 1200 : 2200, token\)/);
assert.match(managedNav, /await _autoLogin\.TryLoginAsync\(\)/);
assert.match(managedNav, /await _autoLogin\.WaitForLoginNavigationAsync\(token\)/);
assert.match(managedNav, /throw new OwnedListLoginPendingException/);
assert.doesNotMatch(managedNav, /@auto:repost|\.click\(|Invoke\(/);

assert.match(login, /public bool IsLoginNavigationBusy/);
assert.match(login, /public async Task WaitForLoginNavigationAsync\(CancellationToken token\)/);
assert.match(login, /_lastLoginNavigationUtc = DateTime\.UtcNow/);
assert.match(automation, /await _autoLogin\.WaitForLoginNavigationAsync\(token\)/);
assert.match(automation, /_autoLogin = new ItemManiaAutoLogin\(/);
assert.match(automation, /if \(_loginRecoveryPauseUntilUtc != DateTime\.MinValue\)/);
assert.match(automation, /_loginRecoveryRecheckRunning \|\| DateTime\.UtcNow < _loginRecoveryPauseUntilUtc/);
assert.match(automation, /await VerifyOwnedListReadableAsync\(loginCheck\.Token\)/);
const transient = automation.split('catch (OwnedListLoginPendingException ex) when (!submitted)')[1]?.split('catch (OperationCanceledException)')[0] || '';
assert.match(transient, /job\.LastStatus = "로그인 전환 대기 \(미제출\)"/);
assert.match(transient, /job\.NextDueUtc = _loginRecoveryPauseUntilUtc/);
assert.doesNotMatch(transient, /RequiresReview = true|제출 결과 확인 필요|재등록 실패 요약/);

console.log('PASS login/repost navigation race isolation and safe read-only retries');
