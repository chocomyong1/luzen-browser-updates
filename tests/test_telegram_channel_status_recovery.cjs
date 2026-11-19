'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8').replace(/\r\n/g, '\n');
const alerts = read('AutomationForm.HourlyQuota.cs');
const host = read('MainForm.TelegramRemote.cs');
const automation = read('AutomationForm.cs');

// A channel ID must not authorize even read-only personal bot commands.
assert.match(alerts, /public string ProtectedStatusChatId/);
assert.match(alerts, /public static string LoadStatusChatId\(\)/);
assert.match(alerts, /long\.TryParse\(statusId, out long id\) && id > 0/);
assert.match(alerts, /long\.TryParse\(oldDestination, out id\) && id > 0/);
assert.match(alerts, /statusId <= 0/);
assert.match(alerts, /PasswordVault\.Protect\(statusChatId\)/);
assert.match(host, /string chatId = TelegramQuotaAlerts\.LoadStatusChatId\(\)/);
assert.match(host, /ReadStatusRequestsAsync\(token, chatId\)/);
assert.match(alerts, /알림 채널 ID/);
assert.match(alerts, /개인 채팅 ID/);

// One failed navigation must not become a Telegram incident.
for (const kind of ['login', 'B VPN', 'owned list'])
  assert.match(automation, new RegExp(`BeginConnectionRecovery\\("${kind}"\\)`));
assert.match(alerts, /_failedConnectionRecoveryProbes\+\+/);
assert.match(alerts, /_failedConnectionRecoveryProbes >= 2/);
assert.match(alerts, /string key = IsTransientConnectionIssue\(category\) \? "연결 장애" : category/);
assert.match(automation, /ConnectionRecoverySucceeded\(\)/);
assert.doesNotMatch(automation, /if \(_schedulerRunning\) NotifyAutomationIssue\("(?:사이트 로그인 복구 대기|관리목록 표시 복구 대기|B VPN 접속 대기)"/);
assert.match(alerts, /category == "제출 결과 확인 필요"/);
assert.match(alerts, /제출: 없음/);
assert.match(alerts, /해당 물품만 재제출 보류/);
console.log('PASS channel/private status separation and recovery-only Telegram alerts');
