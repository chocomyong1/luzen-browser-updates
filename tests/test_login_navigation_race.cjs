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
assert.doesNotMatch(managedNav, /@auto:repost|\.click\(|Invoke\(/);

assert.match(login, /public bool IsLoginNavigationBusy/);
assert.match(login, /public async Task WaitForLoginNavigationAsync\(CancellationToken token\)/);
assert.match(login, /_lastLoginNavigationUtc = DateTime\.UtcNow/);
assert.match(automation, /await _autoLogin\.WaitForLoginNavigationAsync\(token\)/);
assert.match(automation, /_autoLogin = new ItemManiaAutoLogin\(/);

console.log('PASS login/repost navigation race isolation and safe read-only retries');
