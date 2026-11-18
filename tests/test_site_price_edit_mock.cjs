// Exercises the JS actually embedded in the Windows client, without a real
// account or any network submissions. A wrong listing/price must never click.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const script = marker => {
  const markerStart = source.indexOf(`public const string ${marker} = """`);
  assert(markerStart >= 0);
  return source.slice(markerStart).split('"""')[1];
};
const number = '2026070508129767';
const title = '+ 8 + 레벨 겁화, 작열 삽니다 + 즉시대기중 +';

function ownRowCase({ id = number, displayedTitle = title, buttonText = '수정',
  buttonDesc = '수정', buttonHandler = `modifyItem('${number}')`, count = 1 } = {}) {
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
    querySelector: key => key === '.trade_title' ? { textContent: displayedTitle } : null,
    querySelectorAll: key => key === '*' ? actions : key === '.trade_title' ? [titleLink] :
      key.startsWith('a,button,') ? actions : [],
    closest: () => null
  };
  const document = { querySelectorAll: key => key.startsWith('tr,') ? [row] : key === '[onclick]' ? actions : [] };
  const evaluate = (click = true, openTitle = false) => vm.runInNewContext(
    script('OwnedRowScript').replace('__ARG__', JSON.stringify({ number, title, click, openTitle })),
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
  const ambiguous = detailCase({buttonCount: 2});
  assert.equal(ambiguous.evaluate(true).status, 'error');
  assert.equal(ambiguous.clicked, 0);
  const list = detailCase({pathname: '/myroom/buy/buy_regist.html'});
  assert.equal(list.evaluate(true).status, 'error');
  assert.equal(list.clicked, 0);
}

function editFormCase({ id = number, old = '10,000', priceFields = 1,
  heading = '구매금액', finalText = '재등록', host = 'www.itemmania.com',
  buttonHeading = '캐릭터명' } = {}) {
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
  const prices = Array.from({ length: priceFields }, () => new HTMLInputElement(old));
  const buttons = (Array.isArray(finalText) ? finalText : [finalText]).map(label => ({
    innerText: label, value: '', isConnected: true, tagName: 'BUTTON', type: 'button',
    id: 'character-name-save', getAttribute: key => key === 'onclick' ? 'saveCharacterName()' : null,
    closest: key => key === 'form' ? form : key.startsWith('tr,') ? nameRow : null,
    getClientRects: () => [1], click: () => {clicked++; clickedLabels.push(label);}
  }));
  const nameRow = { querySelector: () => ({ textContent: buttonHeading }) };
  const form = { id: 'edit-form', getAttribute: key => key === 'action' ? '/buy/edit_price.html' : null,
    querySelectorAll: key => key.startsWith('button,') ? buttons : [] };
  const headingCell = { textContent: heading };
  const tr = { querySelector: key => key.startsWith('th,') ? headingCell : null,
    querySelectorAll: key => key === 'input' ? [quantity, ...prices] : [] };
  const hidden = { value: id, getAttribute: () => null };
  const document = { querySelectorAll: key => key === 'form' ? [form] :
    key.startsWith('input[type="hidden"]') ? [hidden] :
    key.startsWith('tr,') ? [tr] :
    key.startsWith('button,') ? buttons :
    key === 'input' ? [quantity, ...prices] : [] };
  const evaluate = (submit = true, expected = number, expectedOld = '10000') =>
    vm.runInNewContext(script('EditFormScript').replace('__ARG__', JSON.stringify({
      number: expected, oldPrice: expectedOld, newPrice: '35100', submit
    })), {
      document, HTMLInputElement, Event: class { constructor() {} },
      location: { protocol: 'https:', hostname: host, pathname: '/buy/edit.html' },
      getComputedStyle: () => ({ visibility: 'visible' })
    }, { timeout: 1000 });
  return { evaluate, get clicked() { return clicked; }, get inputEvents() { return inputEvents; },
    clickedLabels, prices };
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
