'use strict';
const fs = require('node:fs');
const assert = require('node:assert/strict');
const path = require('node:path');
const src = fs.readFileSync(path.join(__dirname,'../AppSource/AutomationForm.cs'),'utf8').replace(/\r\n/g,'\n');
const scheduler = src.split('private async Task SchedulerTickAsync()')[1]
  ?.split('private async Task<bool> VerifyOwnedListReadableAsync(')[0] || '';
const login = scheduler.split('if (_loginRecoveryPauseUntilUtc != DateTime.MinValue)')[1]
  ?.split('if (_system == "B" && _bConnectionRecoveryUtc != DateTime.MinValue)')[0] || '';
const vpn = scheduler.split('if (_system == "B" && _bConnectionRecoveryUtc != DateTime.MinValue)')[1]
  ?.split('if (_system != "B" && _ownedListRecoveryUtc != DateTime.MinValue)')[0] || '';
assert(login && vpn, 'B login and VPN recovery lanes are required');

// When the route is not verified, recovery must not remain stuck forever in
// login checks. Start a B-only IP verification lane with a short safe delay.
assert.match(login, /_system == "B" && !DualSystemNetwork\.IsRouteVerified\("B"\)/);
assert.match(login, /_loginRecoveryPauseUntilUtc = DateTime\.MinValue;/);
assert.match(login, /_bConnectionRecoveryUtc = DateTime\.UtcNow\.AddSeconds\(30\);/);
assert.match(login, /BeginConnectionRecovery\("B VPN"\)/);
assert.doesNotMatch(login, /ConnectionRecoveryProbeFailed\("login", _system == "B"/);

// Transport failure during login recovery must switch to B route checks.
assert.match(login, /catch \(OwnedListConnectivityPendingException ex\) when \(_system == "B" && ex\.TransportFailure && _schedulerRunning\)/);
assert.match(login, /_bRepairProxyOnRecovery = true/);
assert.match(login, /BeginConnectionRecovery\("B VPN"\)/);

// Once B's IP is verified, an actual login redirect must stay classified as
// authentication. Do not reset the WARP proxy for login-only redirects.
assert.match(vpn, /await DualSystemNetwork\.VerifyBRouteAsync\(check\.Token\)/);
assert.match(vpn, /catch \(OwnedListLoginPendingException\) when \(verifiedBRoute && _schedulerRunning\)/);
assert.match(vpn, /_bConnectionRecoveryUtc = DateTime\.MinValue;/);
assert.match(vpn, /_loginRecoveryPauseUntilUtc = DateTime\.UtcNow\.AddSeconds\(30\);/);
assert.match(vpn, /BeginConnectionRecovery\("login"\)/);

// A new site action must NEVER be triggered by the recovery branch; only
// verified own-list readbacks may release the queue.
assert.match(login, /await VerifyOwnedListReadableAsync\(loginCheck\.Token\)/);
assert.match(vpn, /await VerifyOwnedListReadableAsync\(check\.Token\)/);
assert.doesNotMatch(login+vpn, /ApplySitePriceAsync\(|ExecuteJobAsync\(|CoreWebView2\.Navigate\(|\.click\(/);
assert(scheduler.indexOf('if (_loginRecoveryPauseUntilUtc != DateTime.MinValue)') <
  scheduler.indexOf('await ExecuteJobAsync(job, false)'),
  'Recovery must precede submission');
console.log('PASS B VPN/login recovery handoff: stalled route re-verification, true auth redirects, no blind submissions');
