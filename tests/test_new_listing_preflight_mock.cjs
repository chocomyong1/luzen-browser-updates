'use strict';

// Exercises the browser-side submit-button resolver shipped in AutomationForm,
// using synthetic pages. No login, network access, or listing submission.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const app = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const host = fs.readFileSync(path.join(__dirname, '../AppSource/MainForm.cs'), 'utf8');
const start = app.indexOf('private async Task<string> ResolveAutoSelectorAsync(');
const end = app.indexOf('""".Replace("__KEY__"', start);
assert(start >= 0 && end > start, 'new-listing selector script missing');
const js = app.slice(start, end).split('"""')[1];

function resolve(key, pathname, labels) {
  const buttons = labels.map((label, i) => ({
    id: `control${i}`, tagName: 'BUTTON', textContent: label, value: '',
    getClientRects: () => [1], disabled: false
  }));
  const document = {
    querySelectorAll: selector => selector.startsWith('button,input[type="submit"]')
      ? buttons : selector.startsWith('#control')
      ? buttons.filter(b => '#' + b.id === selector) : []
  };
  const raw = vm.runInNewContext(js.replace('__KEY__', JSON.stringify(key)), {
    document, location: {pathname},
    getComputedStyle: () => ({visibility: 'visible'}),
    CSS: {escape: value => value}
  }, {timeout: 1000});
  return JSON.parse(raw);
}

for (const [side, key, opposite] of [
  ['/sell/sell_reg.html', 'submitSell', '/buy/buy_reg.html'],
  ['/buy/buy_reg.html', 'submitBuy', '/sell/sell_reg.html']
]) {
  assert.equal(resolve(key, side, ['등록하기']).selector, '#control0',
    `${key}: unique generic submit on the correct editor`);
  assert.equal(resolve(key, side, [key === 'submitSell' ? '판매등록' : '구매등록']).selector, '#control0',
    `${key}: original submit label`);
  assert.match(resolve(key, opposite, ['등록하기']).error, /등록 화면이 아닙니다/,
    `${key}: opposite listing side must be rejected`);
  assert.match(resolve(key, '/myroom/buy/buy_regist.html', ['등록하기']).error, /등록 화면이 아닙니다/,
    `${key}: owned-list page must not be used for a new post`);
  assert.match(resolve(key, side, ['등록하기', '등록하기']).error, /후보 중복/,
    `${key}: ambiguous buttons must not be chosen`);
  assert.match(resolve(key, side, ['저장하기']).error, /자동 매칭 실패/,
    `${key}: unrelated actions must not be treated as submit`);
}

const dryRun = app.slice(app.indexOf('private async Task ExecuteJobAsync('),
  app.indexOf('// A repost button must belong to this EXACT existing listing'));
assert.match(dryRun, /if \(dryRun\)[\s\S]*?ResolveAutoSelectorAsync\(submitSelector\[6\.\.\]/,
  'dry-run must resolve the same final submit control as the real workflow');
assert.match(dryRun, /WaitForElementAsync\(submitSelector/,
  'dry-run must check the submit control is present');
assert.match(dryRun, /\[시험\] 최종 제출 버튼은 누르지 않음:[\s\S]*?break;/,
  'dry-run must exit before invoking the submit control');
assert.match(host, /"신규글 입력시험 \(미제출\)"[\s\S]*?TestSelectedNewRegistrationFromHeaderAsync/);
assert.match(app, /public Task TestSelectedNewRegistrationFromHeaderAsync\(\)[\s\S]*?return RunSelectedAsync\(dryRun: true\);/);
console.log('PASS new sale/buy submit resolver and read-only input-test entry point');
