'use strict';
// Mirrors the FormChecker conditions visible in the user-provided official
// buy_re_reg.js. This is offline: no account, browser POST, or price change.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const src = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const flow = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const js = src.match(/public const string OfficialPricePreflightScript = """([\s\S]*?)""";/)?.[1];
assert(js, 'preflight script must be embedded in the application');
new vm.Script(js);

function check(overrides = {}, config = {}) {
  const values = {
    user_title: '10 10 레벨 겁화 작열 구매합니다',
    user_text: '<p>기존 설명</p>',
    user_goods: 'item', user_goods_type: 'division', game_code: '1835',
    user_quantity_min_tmp: '1', user_quantity_max_tmp: '9995',
    user_quantity_min: '1', user_quantity_max: '9995',
    user_division_unit: '1', user_division_price: '8,000',
    user_character: '기존캐릭터명', user_contactA: 'Y', slctMobile_type: 'N',
    ...overrides
  };
  const fields = Object.fromEntries(Object.entries(values).map(([key, value]) =>
    [key, {value}]));
  const form = {
    checker: config.checker === false ? null : {},
    onsubmit() { return false; },
    getAttribute: key => key === 'id' ? 'frmBuy' : '',
    elements: {namedItem: key => fields[key] || null}
  };
  const document = {forms: {frmBuy: form}};
  const window = {
    editor: {root: {innerHTML: '<p>기존 설명</p>'}},
    DOMPurify: {sanitize: content => content}
  };
  const result = vm.runInNewContext(js, {
    location: {protocol: 'https:',hostname: 'www.itemmania.com',
      pathname: '/myroom/buy/buy_re_reg.html'},
    document, window, initialPrice: 1000, _marketPriceBlocked: !!config.marketBlocked
  });
  return {result, values, fields};
}

assert.equal(check().result.status, 'ready');
assert.equal(check({user_title: '사이트에서 다른 제목으로 변경됨'}).result.status, 'ready',
  'title text is never compared with stored VANTA title or used as ID');
assert.equal(check({user_quantity_min_tmp: '3',user_quantity_max_tmp: '3'}).result.status, 'error');
assert.match(check({user_division_unit: '5'}).result.message, /분할단위/);
assert.match(check({user_division_price: '100'}).result.message, /최소 구매금액/);
assert.match(check({user_character: ''}).result.message, /캐릭터명/);
assert.match(check({user_title: '10 레벨 금지★문자'}).result.message, /허용하지 않는 문자/);
assert.match(check({user_goods_type: 'general'}).result.message, /분할 구매글/);
assert.match(check({}, {marketBlocked: true}).result.message, /시세 제한/);
assert.match(check({}, {checker: false}).result.message, /FormChecker 초기화/);
const emptyText = check({user_text: ''});
assert.equal(emptyText.result.status, 'error', 'preflight must not write the description');
assert.equal(emptyText.fields.user_text.value, '');

const typed = flow.indexOf('await TypeVerifiedSitePriceAsync(');
const afterType = flow.indexOf('SitePriceEdit.OfficialPricePreflightScript', typed);
const click = flow.indexOf('await DispatchVerifiedSitePriceClickAsync(', typed);
assert(typed >= 0 && afterType > typed && click > afterType,
  'validate the NEW price again before any actual submit click');
console.log('PASS first-party FormChecker checks at target price, no title matching or live submit');
