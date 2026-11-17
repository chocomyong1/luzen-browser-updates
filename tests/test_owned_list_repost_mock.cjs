// Dependency-free offline test for the actual JavaScript shipped inside C#.
// No network, real account, or site submission is used.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../AppSource/OwnedListRepost.cs'), 'utf8');
const embedded = source.match(/public const string BrowserScript = """([\s\S]*?)""";/)?.[1];
assert(embedded, 'C# repost browser script missing');

const id = '2026070507571234';
const other = '2026070507579999';
const title = '[1개~9,995개] 10레벨 겁화 보석 구매합니다';
function makeRow(number, name, actionName = '재등록', iconClass = '') {
  const clicks = [];
  const icon = { tagName: 'svg', className: { baseVal: iconClass }, getAttribute: () => null };
  const button = {
    tagName: 'BUTTON', isConnected: true, disabled: false, innerText: actionName, textContent: actionName,
    value: '', title: '', className: '', getClientRects: () => [1],
    getAttribute: key => key === 'title' ? button.title : null,
    querySelectorAll: selector => selector === 'svg,i,img' && iconClass ? [icon] : [],
    click: () => clicks.push(number)
  };
  const titleCell = { tagName: 'TD', getAttribute: () => null };
  const actionCell = { tagName: 'TD', getAttribute: () => null };
  const row = {
    tagName: 'TR', innerText: name, textContent: name,
    closest: () => null,
    getAttribute: key => key === 'data-tid' ? number : null,
    querySelectorAll: selector => selector === '*' ? [titleCell, actionCell, button] :
      selector.startsWith('button,') ? [button] : []
  };
  return { row, button, clicks };
}

// Faithful shape of the authenticated site's Nov 2026 list markup, sanitized
// to synthetic IDs. The icons are CSS spans, not SVG or text buttons.
function makeRealMarkupRow(number, name, reinsertNumber = number, stamp = '11-16 10:53') {
  const clicks = [];
  const icon = (kind, handler, css, desc) => ({
    tagName: 'A', className: css, isConnected: true, disabled: false,
    value: '', innerText: '', textContent: '', getClientRects: () => [1],
    getAttribute: key => ({ onclick: handler, href: 'javascript:;', 'data-desc': desc })[key] || null,
    querySelectorAll: () => [], click: () => clicks.push(kind)
  });
  const actions = [
    icon('repost', `reInsert('${reinsertNumber}','2696');`, 'list_icon reregist', '재등록'),
    icon('hide', `tradeProcess('hideSelect', '${number}')`, 'list_icon hide', '숨기기'),
    icon('delete', `tradeProcess('deleteSelect', '${number}')`, 'list_icon delete', '삭제')
  ];
  const postedAt = { tagName: 'P', textContent: stamp, getAttribute: () => null };
  const row = {
    tagName: 'TR', innerText: `${number} ${name}`, textContent: `${number} ${name}`,
    closest: () => null, getAttribute: () => null,
    querySelectorAll: selector => selector === 'td.flex_box p.f_gray' ? [postedAt] : selector === '*' ? actions :
      selector.startsWith('button,') ? actions : []
  };
  return { row, actions, clicks };
}

function evaluate(rows, { number = id, name = title, click = false,
  pathname = '/myroom/buy/buy_regist.html', hostname = 'www.itemmania.com', siteReinsert } = {}) {
  const document = {
    querySelectorAll: selector => selector === 'tr' ? rows.map(x => x.row) :
      selector.includes('[data-tid]') ? rows.map(x => x.row) : []
  };
  const scope = { document, location: { protocol: 'https:', hostname, pathname },
    getComputedStyle: () => ({ visibility: 'visible' }) };
  if (siteReinsert) scope.window = { reInsert: siteReinsert };
  const js = embedded.replace('__ARG__', JSON.stringify({ number, title: name, click }));
  return vm.runInNewContext(js, scope, { timeout: 1000 });
}

{
  const rows = [makeRow(id, title), makeRow(other, '다른 매물')];
  assert.equal(evaluate(rows).status, 'ready');
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, [id]);
  assert.deepEqual(rows[1].clicks, []);
  assert.equal(evaluate(rows, { click: null }).status, 'found');
  console.log('PASS exact row click, no other listing touched');
}
{
  const rows = [makeRow(other, title)];
  assert.equal(evaluate(rows, { click: true }).status, 'not-found');
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS same title with wrong ID blocked');
}
{
  const rows = [makeRealMarkupRow(id, '사이트에서 변경된 제목')];
  const result = evaluate(rows, { click: true });
  assert.equal(result.status, 'mismatch');
  assert.equal(result.numberRows, 1);
  assert.equal(result.titleRows, 0);
  assert.equal(result.scannedRows, 1);
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS known ID with changed title is reported without reposting');
}
{
  const rows = [makeRealMarkupRow(other, title)];
  const result = evaluate(rows);
  assert.equal(result.status, 'not-found');
  assert.equal(result.numberRows, 0);
  assert.equal(result.titleRows, 1);
  console.log('PASS missing ID and matching title are diagnosed separately');
}
{
  const rows = [makeRow(id, title), makeRow(id, title)];
  assert.equal(evaluate(rows, { click: true }).status, 'error');
  assert(rows.every(x => x.clicks.length === 0));
  console.log('PASS duplicate matching rows blocked');
}
{
  const rows = [makeRow(id, title, '수정')];
  assert.equal(evaluate(rows, { click: true }).status, 'error');
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS unrelated row action blocked');
}
{
  const rows = [makeRow(id, title)];
  assert.equal(evaluate(rows, { click: true, pathname: '/myroom/' }).status, 'error');
  assert.equal(evaluate(rows, { click: true, hostname: 'itemmania.com.attacker.invalid' }).status, 'error');
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS wrong URL and lookalike domains blocked');
}
{
  const rows = [makeRow(id, title), makeRow(other, title)];
  assert.equal(evaluate(rows).status, 'ready');
  console.log('PASS same title belonging to different listing IDs');
}
{
  const rows = [makeRow(id, title, ' ', 'lucide lucide-history'),
    makeRow(other, title, ' ', 'lucide lucide-trash-2')];
  assert.equal(evaluate(rows).status, 'ready');
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, [id]);
  assert.deepEqual(rows[1].clicks, []);
  console.log('PASS screenshot-style icon-only history button on exact row');
}
{
  const rows = [makeRow(id, title, ' ', 'lucide lucide-trash-2')];
  assert.equal(evaluate(rows, { click: true }).status, 'error');
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS trash icon cannot be mistaken for repost');
}
{
  const rows = [makeRow(id, title, ' ')];
  rows[0].button.title = '재등록';
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  console.log('PASS tooltip title used despite whitespace-only icon text');
}
{
  const rows = [makeRealMarkupRow(id, title), makeRealMarkupRow(other, '다른 물품')];
  assert.equal(evaluate(rows).status, 'ready');
  assert.equal(evaluate(rows).stamp, '11-16 10:53');
  assert.equal(evaluate(rows, { click: null }).stamp, '11-16 10:53');
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  assert.deepEqual(rows[1].clicks, []);
  console.log('PASS real ItemMania CSS-span reregist onclick, never hide/delete');
}
{
  const rows = [makeRealMarkupRow(id, title), makeRealMarkupRow(other, '다른 물품')];
  const calls = [];
  const result = evaluate(rows, { click: true, siteReinsert: (number, game) => calls.push([number, game]) });
  assert.equal(result.status, 'clicked');
  assert.equal(result.method, 'native');
  assert.deepEqual(calls, [[id, '2696']]);
  assert.deepEqual(rows[0].clicks, []);
  assert.deepEqual(rows[1].clicks, []);
  console.log('PASS native site reInsert targeted by verified ID, without a DOM click');
}
{
  const rows = [makeRealMarkupRow(id, title, id, 'arbitrary text')];
  assert.equal(evaluate(rows).stamp, '');
  console.log('PASS non-site timestamp cannot establish repost success');
}
{
  const rows = [makeRealMarkupRow(id, title, other)];
  assert.equal(evaluate(rows, { click: true }).status, 'error');
  assert.deepEqual(rows[0].clicks, []);
  console.log('PASS mismatched reInsert listing ID blocked');
}
