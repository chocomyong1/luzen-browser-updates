'use strict';
// Offline safety contract only: neither a website request nor a price edit
// is executed. The first-party AJAX response content is never inspected.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..', 'AppSource');
const flow = fs.readFileSync(path.join(root, 'AutomationForm.SitePriceEdit.cs'), 'utf8');
const observer = fs.readFileSync(path.join(root, 'SitePriceSubmitObserver.cs'), 'utf8');

const buttonMethod = flow.slice(
  flow.indexOf('private async Task DispatchVerifiedSitePriceClickAsync('),
  flow.indexOf('private static bool IsUnsubmittedNewListing(')
);
assert(buttonMethod.includes('Action checkpointBeforeMousePress'));
const verifyButton = buttonMethod.indexOf('SitePriceEdit.VerifyNativeClickScript');
const checkpointCall = buttonMethod.indexOf('checkpointBeforeMousePress();');
const mousePress = buttonMethod.indexOf('await MouseAsync("mousePressed"');
assert(verifyButton > 0 && verifyButton < checkpointCall && checkpointCall < mousePress,
  'Persist the uncertain-result checkpoint after final button verification, before mousePressed');

const flowStart = flow.indexOf('using var submissionObserver = new SitePriceSubmitObserver(');
const priceInput = flow.indexOf('await TypeVerifiedSitePriceAsync(', flowStart);
const singleClick = flow.indexOf('await DispatchVerifiedSitePriceClickAsync(', priceInput);
const reviewState = flow.indexOf('job.RequiresReview = true;', singleClick);
const save = flow.indexOf('_config.Save();', reviewState);
const submitted = flow.indexOf('submitted = true;', save);
const observed = flow.indexOf('var observed = await submissionObserver.WaitAsync(token)', submitted);
assert(flowStart >= 0 && flowStart < priceInput && priceInput < singleClick &&
  singleClick < reviewState && reviewState < save && save < submitted && submitted < observed,
  'Typing or pre-click validation failure must not mark the listing as submitted');
assert.match(flow, /if \(!submitted\)\s*throw new InvalidOperationException\("가격 입력·버튼 최종 확인 단계에서 제출 전 중단:/);
assert(flow.includes(String.raw`\n사이트 단계별 검증: `));

for (const stage of [
  '연락처 검사 요청 이전', '연락처 검사 요청 후 응답 처리 완료 이전',
  '연락처 검사 이후 분류 검사 이전', '분류 검사 요청 후 응답 처리 완료 이전',
  '분류 검사 완료 이후 최종 제출 이전'
]) assert(observer.includes(stage), `Missing transparent stall position: ${stage}`);
assert.match(observer, /_contactStarted = true/);
assert.match(observer, /_contactFinished = true/);
assert.match(observer, /_categoryStarted = true/);
assert.match(observer, /_categoryFinished = true/);
assert.match(observer, /StalledBeforeFinalPost\(\)/);
assert.doesNotMatch(observer, /GetRequestPostData|GetResponseBody|\.postData|\.headers|\.body|Fetch\.enable/);
assert.doesNotMatch(flow.slice(flowStart, observed), /Network\.getRequestPostData|form\.submit\(|fetch\(/);

console.log('PASS precise pre-click checkpoint, user-visible first-party stages and non-retry safety');
