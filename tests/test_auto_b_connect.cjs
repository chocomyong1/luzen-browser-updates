'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const main = fs.readFileSync(path.join(__dirname, '../AppSource/MainForm.cs'), 'utf8');
const net = fs.readFileSync(path.join(__dirname, '../AppSource/DualSystemNetwork.cs'), 'utf8');
const begin = main.indexOf('private async Task SelectSystemAsync(string system)');
const end = main.indexOf('private Control BuildToolbar()', begin);
assert(begin >= 0 && end > begin, 'B 전환 작업이 비동기 자동 확인을 사용하지 않음');
const switchCode = main.slice(begin, end);

assert(switchCode.includes('DualSystemNetwork.Load().ProxyUrl'),
  '저장된 프록시를 다시 읽지 않음');
assert(switchCode.includes('await DualSystemNetwork.VerifyBRouteAsync()'),
  'B 전환 시 안전한 실제 IP 확인이 생략됨');
assert(switchCode.indexOf('await DualSystemNetwork.VerifyBRouteAsync()') <
       switchCode.indexOf('_moduleB.Show()'), 'IP 확인 전에 B 브라우저를 시작함');
assert(switchCode.includes('if (!DualSystemNetwork.IsBRouteVerified) return;'),
  '프록시 확인 실패 후에도 B로 진입할 수 있음');
assert(switchCode.includes('if (selection != _systemSelectionSequence'),
  '자동 확인 중 시스템 A로 전환한 경우의 경합 방지가 없음');
assert(switchCode.includes('OpenSystemBNetworkSettings()') &&
  switchCode.includes('MessageBoxButtons.YesNo'),
  '연결 실패 시에만 수동 설정 창을 여는 경로가 없음');
assert(!switchCode.includes('OpenSystemBNetworkSettings();\n            if'),
  '기존 설정이 있어도 매번 수동 설정으로 이동하는 예전 흐름이 남아 있음');
assert(main.includes('() => _ = SelectSystemAsync("B")'),
  'B 버튼이 자동 확인 흐름에 연결되지 않음');
assert(net.includes('if (string.Equals(direct, b, StringComparison.OrdinalIgnoreCase))'),
  'A와 B가 동일한 공인 IP일 때 연결을 거부하는 검증이 없음');
console.log('PASS automatic B proxy verification after restart, failed-route guard and A/B switching');
