// Exercises the JS actually embedded in the Windows client, without a real
// account or any network submissions. A wrong listing/price must never click.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const identitySource = fs.readFileSync(path.join(__dirname, '../AppSource/OwnedListingTitleIdentity.cs'), 'utf8');
const identityScript = identitySource.match(/public const string BrowserScript = """([\s\S]*?)""";/)?.[1];
assert(identityScript);
const automation = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const script = marker => {
  const markerStart = source.indexOf(`public const string ${marker} = """`);
  assert(markerStart >= 0);
  return source.slice(markerStart).split('"""')[1];
};
const number = '2026070508129767';
const title = '+ 8 + 레벨 겁화, 작열 삽니다 + 즉시대기중 +';

function ownRowCase({ id = number, displayedTitle = title, buttonText = '수정',
  buttonDesc = '수정', buttonHandler = `modifyItem('${number}')`, count = 1,
  minQuantity = '', maxQuantity = '', splitLabel = '' } = {}) {
  let clicked = 0;
  const titleLink = { textContent: displayedTitle, isConnected: true, disabled: false,
    getClientRects: () => [1], matches: () => true, parentElement: null,
    click: () => clicked++ };
  const actions = Array.from({ length: count }, () => ({
    innerText: buttonText, value: '', className: 'list_icon edit', isConnected: true,
    disabled: false, getClientRects: () => [1],
    getAttribute: key => ({ 'data-desc': buttonDesc, onclick: buttonHandler })[key] || null,
    closest: () => row,
    click: () => clicked++
  }));
  const row = {
    innerText: `${id} ${displayedTitle}`, textContent: `${id} ${displayedTitle}`,
    getAttribute: key => key === 'data-tid' ? id : null,
    querySelector: key => key === '.trade_title' ? { textContent: displayedTitle } :
      key === '.sub_trade_title' && splitLabel ? {textContent: splitLabel} : null,
    querySelectorAll: key => key === '*' ? actions : key === '.trade_title' ? [titleLink] :
      key.startsWith('a,button,') ? actions : [],
    closest: () => null
  };
  const document = { querySelectorAll: key => key.startsWith('tr,') ? [row] : key === '[onclick]' ? actions : [] };
  const evaluate = (click = true, openTitle = false) => vm.runInNewContext(
    script('OwnedRowScript').replace('__OWNED_TITLE_IDENTITY__', identityScript)
      .replace('__ARG__', JSON.stringify({ number, title, click, openTitle,
        minQuantity, maxQuantity })),
    { document, location: { protocol: 'https:', hostname: 'www.itemmania.com',
        pathname: '/myroom/buy/buy_regist.html' },
      getComputedStyle: () => ({ visibility: 'visible' }) }, { timeout: 1000 });
  return { evaluate, get clicked() { return clicked; } };
}

{
  const case1 = ownRowCase();
  assert.equal(case1.evaluate(false).status, 'ready');
  assert.equal(case1.clicked, 0);
  assert.equal(case1.evaluate(true).status, 'opened');
  assert.equal(case1.clicked, 1);
}
{
  const wrong = ownRowCase({ id: '2026070508129999' });
  assert.equal(wrong.evaluate(true).status, 'error');
  assert.equal(wrong.clicked, 0);
  const many = ownRowCase({ count: 2 });
  assert.equal(many.evaluate(true).status, 'error');
  assert.equal(many.clicked, 0);
}
{
  const target = ownRowCase({ displayedTitle: '1 ~ 999 레벨 겁화, 작열 삽니다 + 즉시대기중 +',
    minQuantity: '1', maxQuantity: '999' });
  // Different original punctuation is advisory when the signed-in row's
  // authoritative data-tid and range agree. Actual POST still requires the
  // current site price and exact edit-form registration ID.
  assert.equal(target.evaluate(false, true).status, 'ready');
  assert.equal(target.evaluate(true, true).status, 'opened');
  assert.equal(target.clicked, 1);
  const invalidRange = ownRowCase({ displayedTitle: '1 ~ 999 레벨 겁화, 작열 삽니다',
    minQuantity: '1', maxQuantity: '9995', splitLabel: '[1개~999개]' });
  assert.equal(invalidRange.evaluate(true, true).status, 'opened');
  assert.equal(invalidRange.clicked, 1);
  console.log('PASS price-edit navigation accepts stale title/quantity with exact ID; site price verified separately');
}
{
  const titleRoute = ownRowCase({ buttonText: '', buttonDesc: '', buttonHandler: '' });
  assert.equal(titleRoute.evaluate(false, true).status, 'ready');
  assert.equal(titleRoute.evaluate(true, true).status, 'opened');
  assert.equal(titleRoute.clicked, 1);
  const wrong = ownRowCase({ id: '2026070508129999' });
  assert.equal(wrong.evaluate(true, true).status, 'error');
  assert.equal(wrong.clicked, 0);
}

function detailCase({ pageId = number, pageTitle = title, buttonCount = 1,
  pathname = '/myroom/buy/buy_regist_view.html' } = {}) {
  let clicked = 0;
  const buttons = Array.from({length: buttonCount}, () => ({
    innerText: '수정하기', value: '', isConnected: true, disabled: false,
    getClientRects: () => [1], getAttribute: () => null,
    click: () => clicked++
  }));
  const document = { body: {innerText: `#${pageId} ${pageTitle}`},
    querySelectorAll: () => buttons };
  const evaluate = click => vm.runInNewContext(
    script('DetailEditScript').replace('__ARG__', JSON.stringify({number, title, click})),
    { document, location: {protocol: 'https:', hostname: 'www.itemmania.com',
      pathname, search: ''},
    getComputedStyle: () => ({visibility: 'visible'}) }, {timeout: 1000});
  return {evaluate, get clicked() {return clicked;}};
}
{
  const good = detailCase();
  assert.equal(good.evaluate(false).status, 'ready');
  assert.equal(good.clicked, 0);
  assert.equal(good.evaluate(true).status, 'opened');
  assert.equal(good.clicked, 1);
  const wrong = detailCase({pageId: '2026070508129999'});
  assert.equal(wrong.evaluate(true).status, 'error');
  assert.equal(wrong.clicked, 0);
  const changedTitle = detailCase({pageTitle: '제목이 사이트에서 변경된 등록글'});
  assert.equal(changedTitle.evaluate(true).status, 'opened');
  assert.equal(changedTitle.clicked, 1);
  const ambiguous = detailCase({buttonCount: 2});
  assert.equal(ambiguous.evaluate(true).status, 'error');
  assert.equal(ambiguous.clicked, 0);
  const list = detailCase({pathname: '/myroom/buy/buy_regist.html'});
  assert.equal(list.evaluate(true).status, 'error');
  assert.equal(list.clicked, 0);
}

function editFormCase({ id = number, old = '10,000', priceFields = 1,
  heading = '구매금액', finalText = '재등록', host = 'www.itemmania.com',
  buttonHeading = '캐릭터명', approvedSubmit = false,
  pathname = approvedSubmit ? '/myroom/buy/buy_re_reg.html' : '/buy/edit.html',
  formId = 'frmBuy', priceFieldName = 'user_division_price',
  formTitle = title, includeTitleField = true,
  nativeSubmitCount = 1, submitId = 'ok_btn', submitClass = 'buy_re_reg',
  includeFormId = true, formAction = '', formMethod = 'post' } = {}) {
  let clicked = 0;
  const clickedLabels = [];
  let inputEvents = 0;
  class HTMLInputElement {
    constructor(value) { this.value = value; this.type = 'text'; this.tagName = 'INPUT'; this.disabled = false; this.readOnly = false; this.isConnected = true; }
    set value(v) { this._value = v; }
    get value() { return this._value; }
    getClientRects() { return [1]; }
    dispatchEvent() { inputEvents++; return true; }
    closest(name) { return name === 'form' ? form : null; }
  }
  const quantity = new HTMLInputElement('1');
  const titleInput = new HTMLInputElement(formTitle);
  titleInput.name = 'user_title';
  titleInput.getAttribute = key => key === 'name' ? titleInput.name : null;
  const prices = Array.from({ length: priceFields }, () => new HTMLInputElement(old));
  for (const p of prices) {
    p.name = priceFieldName;
    p.getAttribute = key => key === 'name' ? p.name : null;
  }
  const buttons = (Array.isArray(finalText) ? finalText : [finalText]).map(label => ({
    innerText: label, value: '', isConnected: true, tagName: 'BUTTON', type: 'button',
    id: 'character-name-save', getAttribute: key => key === 'onclick' ? 'saveCharacterName()' : null,
    closest: key => key === 'form' ? form : key.startsWith('tr,') ? nameRow : null,
    getClientRects: () => [1], click: () => {clicked++; clickedLabels.push(label);}
  }));
  const nameRow = { querySelector: () => ({ textContent: buttonHeading }) };
  const nativeSubmits = Array.from({length: approvedSubmit ? nativeSubmitCount : 0}, () => ({
    innerText: '재등록', value: '', isConnected: true, tagName: 'BUTTON', type: 'submit',
    getAttribute: key => ({id: submitId, class: submitClass, type: 'submit'})[key] || null,
    closest: key => key === 'form' ? form : null,
    getClientRects: () => [1], click: () => {clicked++; clickedLabels.push('REAL_SITE_RE_REG');}
  }));
  const allButtons = [...buttons, ...nativeSubmits];
  const hidden = { value: id, getAttribute: () => null };
  const formAttrs = {id: formId, action: formAction, method: formMethod};
  const form = { tagName: 'FORM', dataset: {}, getAttribute: key => formAttrs[key] || null,
    requestSubmit: () => assert.fail('Native form submission must not be used in this transport'),
    querySelectorAll: key => key === 'input[type="hidden"]' ? (includeFormId ? [hidden] : []) :
      key === 'input,textarea' ? (includeTitleField ? [titleInput] : []) :
      key === 'button[type="submit"]' ? nativeSubmits : key.startsWith('button,') ? allButtons : [] };
  class FormData {
    constructor(target) {
      assert.equal(target, form);
      this.fields = new Map([['user_division_price', prices[0].value],
        ['registration_id', id], ['other_hidden_field', 'private value']]);
      if (includeTitleField) this.fields.set('user_title', titleInput.value);
    }
    has(name) { return this.fields.has(name); }
    get(name) { return this.fields.get(name); }
    getAll(name) { return this.fields.has(name) ? [this.fields.get(name)] : []; }
    append(name, value) { this.fields.set(name, value); }
    keys() { return this.fields.keys(); }
    values() { return this.fields.values(); }
  }
  const fetchCalls = [];
  const networkFetch = (url, options) => {
    assert.ok(options.body instanceof FormData);
    fetchCalls.push({url, options});
    clicked++; clickedLabels.push('REAL_MULTIPART_POST');
    return Promise.resolve({status:200});
  };
  const headingCell = { textContent: heading };
  const tr = { querySelector: key => key.startsWith('th,') ? headingCell : null,
    querySelectorAll: key => key === 'input' ? [quantity, ...prices] : [] };
  const document = { querySelectorAll: key => key === 'form' ? [form] :
    key.startsWith('input[type="hidden"]') ? [hidden] :
    key.startsWith('tr,') ? [tr] :
    key.startsWith('button,') ? allButtons :
    key === 'input' ? [quantity, ...prices] : [] };
  const evaluate = (submit = true, expected = number, expectedOld = '10000') =>
    vm.runInNewContext(script('EditFormScript').replace('__ARG__', JSON.stringify({
      number: expected, oldPrice: expectedOld, newPrice: '35100', submit,
      expectedTitle: title
    })), {
      document, HTMLInputElement, FormData, fetch: networkFetch, Event: class { constructor() {} },
      location: { protocol: 'https:', hostname: host, pathname, search: '' },
      getComputedStyle: () => ({ visibility: 'visible' })
    }, { timeout: 1000 });
  return { evaluate, get clicked() { return clicked; }, get inputEvents() { return inputEvents; },
    clickedLabels, prices, formAttrs, fetchCalls };
}

{
  const audited = editFormCase({approvedSubmit: true, finalText: ['저장', '수정하기']});
  assert.equal(audited.evaluate(false).status, 'blocked');
  assert.equal(audited.clicked, 0);
  assert.equal(audited.prices[0].value, '10,000');
  assert.equal(audited.evaluate(true).status, 'blocked');
  assert.match(audited.evaluate(true).message, /제목 보호 모드/);
  assert.equal(audited.prices[0].value, '10,000');
  assert.equal(audited.inputEvents, 0);
  assert.deepEqual(audited.clickedLabels, []);
  assert.equal(audited.fetchCalls.length, 0, 'Never resubmit the whole form to change only its price');
  assert.doesNotMatch(audited.evaluate(false).message, /private value/);
  assert.equal(audited.evaluate(true, number, '35100').status, 'error');
  assert.equal(audited.fetchCalls.length, 0);
  assert.match(source, /AutomaticPriceSubmissionEnabled\s*=>\s*false/);
  assert.match(automation, /!SitePriceEdit\.AutomaticPriceSubmissionEnabled/);
  console.log('PASS approved frmBuy cannot send even one multipart POST while title safety is active');
}

{
  const modifiedTitle = editFormCase({ approvedSubmit: true, formTitle: '1 ~ 999 9 9 레벨 겁화 작열 구매합니다' });
  assert.equal(modifiedTitle.evaluate(false).status, 'error');
  assert.equal(modifiedTitle.evaluate(true).status, 'error');
  assert.equal(modifiedTitle.fetchCalls.length, 0);
  const unknownTitle = editFormCase({ approvedSubmit: true, includeTitleField: false });
  assert.equal(unknownTitle.evaluate(false).status, 'error');
  assert.equal(unknownTitle.evaluate(true).status, 'error');
  assert.equal(unknownTitle.fetchCalls.length, 0);
  console.log('PASS title mismatch or missing title field blocks price re-registration POST');
}

{
  const safe = editFormCase();
  assert.equal(safe.evaluate(false).status, 'diagnostic');
  assert.equal(safe.clicked, 0);
  assert.equal(safe.evaluate(true).status, 'diagnostic');
  assert.equal(safe.prices[0].value, '10,000');
  assert.equal(safe.inputEvents, 0);
  assert.equal(safe.clicked, 0);
  console.log('PASS exact owned listing and old price → read-only diagnosis, no submission');
}
{
  const mixed = editFormCase({finalText: ['재등록', '수정하기']});
  assert.equal(mixed.evaluate(false).status, 'diagnostic');
  const diagnostic = mixed.evaluate(true);
  assert.equal(diagnostic.status, 'diagnostic');
  assert.match(diagnostic.message, /캐릭터명/);
  assert.match(diagnostic.message, /saveCharacterName/);
  assert.equal(mixed.clicked, 0);
  assert.equal(mixed.inputEvents, 0);
  const duplicate = editFormCase({finalText: ['수정하기', '수정하기']});
  assert.equal(duplicate.evaluate(true).status, 'diagnostic');
  assert.equal(duplicate.clicked, 0);
  assert.equal(duplicate.prices[0].value, '10,000');
  console.log('PASS misleading character-name 수정하기 and duplicate buttons never click');
}
{
  const many = editFormCase({finalText: ['저장', ...Array(56).fill('이용안내'), '수정하기']});
  const diagnosis = many.evaluate(true);
  assert.equal(diagnosis.status, 'diagnostic');
  assert.equal(diagnosis.candidates, 58);
  assert.match(diagnosis.message, /작업 후보\(뒤쪽 우선\)/);
  assert.match(diagnosis.message, /#58 수정하기/);
  assert.equal(many.clicked, 0);
  assert.equal(many.inputEvents, 0);
  assert.equal(many.prices[0].value, '10,000');
  assert.match(automation, /Clipboard\.SetText\(diagnosis\)/);
  assert.match(automation, /if \(form\.Status == "diagnostic"\)/);
  console.log('PASS 58 controls: trailing edit candidate visible, copyable, no click');
}
{
  for (const change of [
    {pathname:'/myroom/buy/buy_regist_view.html'},
    {formId:'frmSell'},
    {priceFieldName:'character_name'},
    {submitId:'character_name_save'},
    {submitClass:'btn_name_save'},
    {nativeSubmitCount:2},
    {includeFormId:false},
    {formAction:'/myroom/wrong_action.php'},
    {formMethod:'get'}
  ]) {
    const invalid = editFormCase({approvedSubmit:true, ...change});
    assert.notEqual(invalid.evaluate(true).status, 'submitted', JSON.stringify(change));
    assert.equal(invalid.clicked, 0);
    assert.equal(invalid.inputEvents, 0);
    assert.equal(invalid.prices[0].value, '10,000');
  }
  console.log('PASS wrong URL, form, price field, submit ID/class, duplicates and missing owned ID never submit');
}
{
  const wrongId = editFormCase({ id: '2026070508129999' });
  assert.equal(wrongId.evaluate().status, 'error');
  assert.equal(wrongId.clicked, 0);
  const wrongPrice = editFormCase({ old: '30,000' });
  assert.equal(wrongPrice.evaluate().status, 'error');
  assert.equal(wrongPrice.clicked, 0);
  const ambiguous = editFormCase({ priceFields: 2 });
  assert.equal(ambiguous.evaluate().status, 'error');
  assert.equal(ambiguous.clicked, 0);
  const badHost = editFormCase({ host: 'itemmania.com.attacker.invalid' });
  assert.equal(badHost.evaluate().status, 'error');
  assert.equal(badHost.clicked, 0);
  console.log('PASS wrong listing, stale price, multiple inputs, fake domain blocked');
}
