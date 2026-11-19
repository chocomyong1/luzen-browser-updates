'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const automation = read('AutomationForm.cs');
const alerts = read('AutomationForm.HourlyQuota.cs');

assert.match(alerts, /TelegramQuotaAlerts\.Load\(\)/);
assert.match(alerts, /PasswordVault\.Protect\(token\)/);
assert.match(alerts, /PasswordVault\.Protect\(chatId\)/);
assert.match(alerts, /api\.telegram\.org\/bot/);
assert.match(alerts, /private void NotifyAutomationIssue\(/);
assert.match(alerts, /_lastTelegramIssueUtc\.TryGetValue\(key, out var last\)/);
assert.match(alerts, /TimeSpan\.FromMinutes\(15\)/);
assert.match(alerts, /SafeTelegramIssueText\(details, 115\)/);
assert.match(alerts, /password\|token\|cookie\|authorization\|session/);
assert.match(alerts, /오류 알림은 채널로, \/status 현황 조회는 개인 채팅으로 분리/);
assert.match(alerts, /_failedConnectionRecoveryProbes >= 2/);
assert.match(alerts, /제출: 없음 · 2분마다 자동 재확인/);

assert.match(automation, /catch \(OperationCanceledException\) when \(!_schedulerRunning\)/);
assert.match(automation, /NotifyAutomationIssue\("자동등록 예기치 못한 중단", ex\.Message\)/);
assert.match(automation, /ConnectionRecoveryProbeFailed\("B VPN", "B VPN 연결 장애"/);
assert.match(automation, /NotifyAutomationIssue\("예약 가격 수정 보류"/);
assert.match(automation, /ConnectionRecoveryProbeFailed\("login", "사이트 로그인 복구 대기"/);
assert.match(automation, /ConnectionRecoveryProbeFailed\("B VPN", "B VPN 접속 대기"/);
assert.match(automation, /NotifyAutomationIssue\(submitted \? "제출 결과 확인 필요" : "등록 작업 오류"/);
const manualMethods = automation.slice(automation.indexOf('private void PauseScheduler()'),
  automation.indexOf('private bool CanRunNow('));
assert.doesNotMatch(manualMethods, /NotifyAutomationIssue/);

console.log('PASS delayed connectivity alerts, dedupe, protected channel and manual-stop exclusion');
