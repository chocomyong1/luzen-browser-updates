'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const list = source('AutomationForm.MyListings.cs');
const runner = source('AutomationForm.cs');
const split = source('AutomationForm.SplitStatus.cs');
const errorCopy = source('ErrorLogCopy.cs');
const presentation = source('LogPresentation.cs');

const navigate = list.split('private async Task NavigateOwnedListAsync(')[1];
assert(navigate, 'owned-list navigation missing');
assert.match(navigate, /bool sawTransportFailure = false/);
assert.match(navigate, /response\.HttpStatusCode == 0/);
assert.match(navigate, /CoreWebView2WebErrorStatus\.ErrorHttpInvalidServerResponse/);
assert.match(navigate, /if \(_system == "B" && sawTransportFailure\)/);
assert.match(navigate, /throw new OwnedListConnectivityPendingException/);
assert.match(list, /throw new OwnedListLoginPendingException/);
assert.doesNotMatch(navigate, /click\(|Invoke\(/);

const locate = runner.split('private async Task<OwnedListRepostResult> LocateOwnedListRepostAsync(')[1]
  ?.split('private static bool IsTransientOwnedListPageError(')[0] || '';
assert.match(locate, /if \(_system == "B" && IsTransientOwnedListPageError\(row\)\)/);
assert.match(locate, /throw new OwnedListConnectivityPendingException/);

const retry = runner.split('private async Task SchedulerTickAsync()')[1]
  ?.split('private async Task VerifyOneHeldListingInBackgroundAsync(')[0] || '';
assert.match(retry, /_system == "B" && _bConnectionRecoveryUtc != DateTime\.MinValue/);
assert.match(retry, /if \(_bConnectionRecheckRunning \|\| DateTime\.UtcNow < _bConnectionRecoveryUtc\) return/);
assert.match(retry, /await DualSystemNetwork\.VerifyBRouteAsync\(\)/);
assert.match(retry, /_bConnectionRecoveryUtc = DateTime\.UtcNow\.AddMinutes\(2\)/);
assert(retry.indexOf('await DualSystemNetwork.VerifyBRouteAsync()') < retry.indexOf('await ExecuteJobAsync(job, false)'),
  'B VPN recheck must precede next submission');

const pending = runner.split('catch (OwnedListConnectivityPendingException ex) when (!submitted && _system == "B")')[1]
  ?.split('catch (OperationCanceledException)')[0] || '';
assert.match(pending, /_bConnectionRecoveryUtc = DateTime\.UtcNow\.AddSeconds\(90\)/);
assert.match(pending, /job\.NextDueUtc = _bConnectionRecoveryUtc/);
assert.match(pending, /\(미제출\)/);
assert.doesNotMatch(pending, /RequiresReview = true|MarkAttempt|\.click\(|제출 결과 확인 필요/);
assert.match(split, /VPN 연결 대기/);
assert.match(errorCopy, /시스템: /);
assert.match(presentation, /B VPN 접속 대기:/);
console.log('PASS B management HTTP 0 isolation, backoff, IP recheck and per-system diagnostics');
