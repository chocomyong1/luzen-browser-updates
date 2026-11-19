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
assert.match(screen, /추적기 자동 연결/);
assert.match(screen, /연결 코드 입력 불필요/);
assert.doesNotMatch(screen, /CopyMarketPairingToken|Clipboard\.SetText|Tampermonkey 'VANTA 연결 코드 설정'/);
assert.match(screen, /개별 승인 후 그 게시글의 다음 정상 순번에만 반영됩니다/);
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
assert.match(bridge, /"GET \/pair HTTP\/1\.1"/);
assert.match(bridge, /Header\(lines, "X-Vanta-Client"\) != "gem-tracker"/);
assert.match(bridge, /Header\(lines, "Host"\) !=/);
assert.doesNotMatch(bridge, /ItemManiaAccountStore\.|TelegramToken|SitePriceEdit\.|ExecuteScriptAsync\(|new HttpClient/);

assert.match(scheduler, /job\.PendingSitePriceTarget/);
assert.match(scheduler, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true\)/);
assert.match(workflow, /dotnet run --project tests\/MarketBridgeTests\/MarketBridgeTests\.csproj/);
console.log('PASS VANTA market analysis screen, local-only listener, no automatic price mutation');
