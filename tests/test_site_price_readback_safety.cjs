'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const code = fs.readFileSync(path.join(__dirname,
  '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const start = code.indexOf('private async Task<bool> ApplySitePriceAsync(');
const pending = code.indexOf('bool unresolvedPriceAttempt', start);
const submit = code.indexOf('BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true)', start);
assert(start >= 0 && pending > start && submit > pending,
  '가격 확인 대기 차단은 실제 사이트 제출보다 먼저 실행돼야 합니다');
assert.match(code.slice(pending, submit), /if \(job\.RequiresReview \|\| unresolvedPriceAttempt\)/);
assert.match(code.slice(pending, submit), /await InspectPendingSitePriceOnlyAsync\(job, desired\)/);

const scanStart = code.indexOf('private async Task InspectPendingSitePriceOnlyAsync(');
const confirmStart = code.indexOf('private async Task<bool> ConfirmChangedSitePriceAsync(', scanStart);
const inspect = code.slice(scanStart, confirmStart);
assert(scanStart > submit && confirmStart > scanStart);
assert.match(inspect, /ReadSitePriceAfterRepostAsync\(job, target, cancellation\.Token\)/);
assert.doesNotMatch(inspect, /\.click\(\)|BuildEditFormScript\(|MarkAttempt|\.ListingNumber\s*=/,
  '보류 글은 읽기 전용으로만 확인해야 합니다');

const readStart = code.indexOf('private async Task<SitePriceReadback> ReadSitePriceAfterRepostAsync(', confirmStart);
const readEnd = code.indexOf('private void HandleSitePriceEditScriptDialog(', readStart);
const read = code.slice(readStart, readEnd);
assert(readStart > confirmStart && readEnd > readStart);
assert.match(read, /MyListingsImport\.ExtractPageScript/);
assert.match(read, /matchingOld\.Add\(row\)/);
assert.match(read, /newCandidates\.Add\(row\.ListingNumber, row\)/);
assert.match(read, /새 등록번호 후보/);
assert.doesNotMatch(read, /job\.ListingNumber\s*=|\.click\(\)|BuildEditFormScript\(/,
  '사이트 번호 후보는 자동으로 재연결하거나 제출할 수 없습니다');
assert.match(code.slice(confirmStart, readStart), /return readback\.ExactIdVerified/,
  '새 등록번호 후보만으로 자동 성공 처리해서는 안 됩니다');

console.log('PASS pending price submissions block duplicates; read-only list scan reports new ID candidates only');
