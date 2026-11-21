'use strict';
// Offline regression: clearing a local price request cannot release an
// uncertain POST or change any published item; zero live requests.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');
const form = read('AutomationForm.cs');
const scheduler = read('AutomationForm.PriceReservations.cs');
const ui = read('MainForm.PriceReservations.cs');

assert.match(form, /!SitePriceEdit\.AutomaticPriceSubmissionEnabled &&\s*!string\.IsNullOrWhiteSpace\(oldJob\.PendingSitePriceTarget\)/);
assert.match(form, /oldJob\.LastStatus == "제목 보호 모드: 가격 예약 자동 제출 중지"/);
assert.match(form, /!oldJob\.PendingRepostAttemptUtc\.HasValue/);
assert.match(form, /string\.IsNullOrWhiteSpace\(oldJob\.PriceEditSiteTitleSnapshot\)/);
assert.match(form, /oldJob\.Enabled = false;\s*oldJob\.RequiresReview = false;/);
assert.match(form, /SitePriceEdit\.AutomaticPriceSubmissionEnabled \? "가격 예약" : "미적용 가격 요청"/);
assert.match(form, /실행 가능한 물품이 0건/);
assert.match(form, /자동 ON \{autoOn\}건 · 확인 필요 \{review\}건/);

const cancel = scheduler.slice(scheduler.indexOf('internal bool TryCancelUnsubmittedPriceReservation('),
  scheduler.indexOf('internal IReadOnlyList<PriceReservationSnapshot> CurrentPriceReservations('));
assert.match(cancel, /job\.RequiresReview && !legacyHold/);
assert.match(cancel, /job\.PendingRepostAttemptUtc\.HasValue/);
assert.match(cancel, /!string\.IsNullOrWhiteSpace\(job\.PriceEditSiteTitleSnapshot\)/);
assert.match(cancel, /job\.PendingSitePriceTarget = ""/);
assert.match(cancel, /job\.RequiresReview = false/);
assert.match(cancel, /job\.Enabled = false/);
assert.match(cancel, /_config\.Save\(\)/);
assert.match(cancel, /job\.PendingSitePriceTarget = beforeTarget/);
assert.doesNotMatch(cancel, /ExecuteScriptAsync|ApplySitePriceAsync|SubmitAsync|\.click\(|Fetch\.|Network\.|HttpClient|\.Price\s*=/);

assert.match(ui, /"선택 요청 취소"/);
assert.match(ui, /TryCancelUnsubmittedPriceReservation\(selected\.Job, out var problem\)/);
assert.match(ui, /MessageBoxButtons\.YesNo/);
assert.match(ui, /사이트 가격과 저장된 물품 가격은 바꾸지 않습니다/);
assert.match(ui, /edit\.Enabled = selected is not null && SitePriceEdit\.AutomaticPriceSubmissionEnabled/);
assert.doesNotMatch(ui, /ExecuteScriptAsync|ApplySitePriceAsync|\.submit\(/);
console.log('PASS safe stale-price holds, explicit local-only cancellation and per-system empty-state explanation');
