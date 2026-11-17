'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');

const net = source('DualSystemNetwork.cs');
const main = source('MainForm.cs');
const ui = source('MainForm.DualSystem.cs');
const login = source('ItemManiaLogin.cs');
const settings = source('Settings.cs');

// Three independent persistent folders, cookies, and encrypted accounts.
assert.match(settings, /Path\.Combine\(UserDirectory, "SystemB"\)/);
assert.match(settings, /Path\.Combine\(UserDirectory, "SystemC"\)/);
assert.match(login, /Path\.Combine\(Settings\.AutomationDirectory\(system\), "itemmania_account\.json"\)/);
assert.match(login, /Path\.Combine\(Settings\.AutomationDirectory\(key\), "ItemManiaWebView2"\)/);

// B and C have different persisted local proxies; C's endpoint cannot equal B's.
assert.match(net, /"system-b-network\.json" : "system-c-network\.json"/);
assert.match(net, /string\.Equals\(normalizedB, Load\("C"\)\.ProxyUrl/);
assert.match(net, /string\.Equals\(direct, routed,/);
assert.match(net, /string\.Equals\(b, c,/);
assert.match(net, /throw new InvalidOperationException\("B의 외부 IP를 확인할 수 없어 C의 독립 연결을 검증하지 못했습니다/);
assert.match(net, /Verified\["C"\] = /);
assert.match(net, /if \(system == "B"\) Verified\.Remove\("C"\)/);
assert.match(login, /AdditionalBrowserArguments = "--no-proxy-server"/);

// A proxy failure cannot silently start an unverified B/C session.
const switchStart = main.indexOf('private async Task SelectSystemAsync(string system)');
const switchEnd = main.indexOf('private Control BuildToolbar()', switchStart);
const switching = main.slice(switchStart, switchEnd);
assert(switchStart > 0 && switchEnd > switchStart);
assert.match(switching, /if \(!DualSystemNetwork\.IsRouteVerified\(system\)\) return;/);
assert(switching.indexOf('await DualSystemNetwork.VerifyCRouteAsync()') < switching.indexOf('_moduleC.Show()'));
assert.match(ui, /new ItemManiaAccountForm\(system\)/);
assert(!ui.includes('ItemManiaAccountStore.Save(a, "B")'), 'A 계정이 B로 무단 복사될 수 있음');

console.log('PASS A/B/C profile separation and three distinct public-IP safety checks');
