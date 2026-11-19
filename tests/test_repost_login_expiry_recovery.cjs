'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
// Windows Git checkouts may use CRLF even when the local test runner uses LF.
const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8').replace(/\r\n/g, '\n');
const runner = source('AutomationForm.cs');
const login = source('ItemManiaLogin.cs');
const status = source('AutomationForm.SplitStatus.cs');

// A site alert after a repost click must block ALL further submissions on
// this profile. Waiting 30 seconds without rechecking login is insufficient.
const dialog = runner.split('private void OnItemManiaScriptDialog(')[1]
  ?.split('private async Task ConfirmExactRegistrationDialogAsync(')[0] || '';
assert.match(dialog, /officialLoginRejected/);
assert.match(dialog, /compactMessage\.Contains\("로그인후이용해주세요"/);
assert.match(dialog, /_repostLoginRejected = true/);
assert.match(dialog, /_repostDialogBlocked = "사이트 로그인 세션이 만료/);
assert.match(dialog, /e\.Accept\(\); \/\/ Dismiss only the informational alert/);
assert(dialog.indexOf('if (officialLoginRejected)') < dialog.indexOf('if (officialCooldownAlert)'),
  'login rejection must be classified before generic cooldown / success handling');

const failure = runner.split('catch (Exception ex)\n        {\n            if (submitted && _repostLoginRejected)')[1]
  ?.split('finally\n        {')[0] || '';
assert(failure, 'post-submit login rejection must have a dedicated handler');
assert.match(failure, /_loginRecoveryPauseUntilUtc = DateTime\.UtcNow\.AddSeconds\(30\)/);
assert.match(failure, /_autoLogin\?\.ResetAfterOfficialLoginRejection\(\)/);
assert.match(failure, /job\.RequiresReview = true/);
assert.match(failure, /job\.NextDueUtc = submitted \? DateTime\.MaxValue/);
assert.doesNotMatch(failure, /_repostLoginRejected[\s\S]*job\.RequiresReview = false/);

const scheduler = runner.split('private async Task SchedulerTickAsync()')[1]
  ?.split('private async Task<bool> VerifyOwnedListReadableAsync(')[0] || '';
assert.match(scheduler, /if \(_loginRecoveryPauseUntilUtc != DateTime\.MinValue\)/);
assert.match(scheduler, /_loginRecoveryRecheckRunning/);
assert.match(scheduler, /_loginRecoveryPauseUntilUtc = DateTime\.UtcNow\.AddMinutes\(2\)/);
assert.match(scheduler, /await VerifyOwnedListReadableAsync\(loginCheck\.Token\)/);
assert.match(scheduler, /_loginRecoveryPauseUntilUtc = DateTime\.MinValue/);
assert(scheduler.indexOf('if (_loginRecoveryPauseUntilUtc != DateTime.MinValue)') <
  scheduler.indexOf('await ExecuteJobAsync(job, false)'), 'auth recovery must precede any job');
assert.match(login, /public void ResetAfterOfficialLoginRejection\(\)/);
assert.match(login, /_lastAttemptUtc = DateTime\.MinValue/);
assert.match(status, /로그인 복구 대기/);

// Normalized forms of the observed official login-required message.
const isLoginRequired = s => {
  const compact = [...s].filter(ch => !/\s/.test(ch)).join('');
  return compact.includes('로그인후이용해주세요') || compact.includes('로그인이필요합니다');
};
assert(isLoginRequired('로그인 후 이용해주세요.'));
assert(isLoginRequired('로그인이 필요합니다.'));
assert(!isLoginRequired('재등록이 완료되었습니다.'));
assert(!isLoginRequired('해당 서버에 아직 등록하실 수 없습니다.'));

console.log('PASS post-submit login rejection pauses all jobs, safely holds result and probes fresh owned-list login');
