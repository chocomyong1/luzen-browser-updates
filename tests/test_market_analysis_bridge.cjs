'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '..', p), 'utf8');
const main = read('AppSource/MainForm.cs');
const screen = read('AppSource/MainForm.MarketAnalysis.cs');
const bridge = read('AppSource/MarketBridgeServer.cs');
const integrated = read('AppSource/MainForm.IntegratedEditor.cs');
const scheduler = read('AppSource/AutomationForm.cs');
const workflow = read('.github/workflows/build-windows.yml');

assert.match(main, /"시세 분석", ToggleMarketView/);
assert.match(main, /StartMarketBridge\(\)/);
assert.match(screen, /DarkScrollbarTheme\.Attach\(_marketGrid\)/);
assert.match(screen, /"통합 편집 열기"/);
assert.match(screen, /가격을 바꾸려면 통합 편집에서 저장하세요/);
assert.match(screen, /TimeSpan age = DateTime\.UtcNow - _marketReceivedUtc/);
assert.match(screen, /stale = age\.TotalSeconds > 120/);
assert.match(integrated, /if \(_marketVisible\) DisableMarketView\(\)/);

assert.match(bridge, /new TcpListener\(IPAddress\.Loopback, _port\)/);
assert.match(bridge, /RandomNumberGenerator\.GetBytes\(32\)/);
assert.match(bridge, /CryptographicOperations\.FixedTimeEquals/);
assert.match(bridge, /MaxBodyBytes = 65536/);
assert.match(bridge, /snapshot\.Schema != 1/);
assert.match(bridge, /snapshot\.ObservedAt < now - 5 \* 60_000/);
assert.match(bridge, /"POST \/market HTTP\/1\.1"/);
assert.doesNotMatch(bridge, /ItemManiaAccountStore\.|TelegramToken|SitePriceEdit\.|ExecuteScriptAsync\(|new HttpClient/);

assert.match(scheduler, /job\.PendingSitePriceTarget/);
assert.match(scheduler, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true\)/);
assert.match(workflow, /dotnet run --project tests\/MarketBridgeTests\/MarketBridgeTests\.csproj/);
console.log('PASS VANTA market analysis screen, local-only listener, no automatic price mutation');
