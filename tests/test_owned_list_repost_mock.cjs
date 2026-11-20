// Dependency-free offline test for the actual JavaScript shipped inside C#.
// No network, real account, or site submission is used.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.join(__dirname, '../AppSource/OwnedListRepost.cs'), 'utf8');
const embedded = source.match(/public const string BrowserScript = """([\s\S]*?)""";/)?.[1];
assert(embedded, 'C# repost browser script missing');
const identitySource = fs.readFileSync(path.join(__dirname, '../AppSource/OwnedListingTitleIdentity.cs'), 'utf8');
const identityScript = identitySource.match(/public const string BrowserScript = """([\s\S]*?)""";/)?.[1];
assert(identityScript, 'shared title identity script missing');

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
    querySelector: key => key === '.trade_title' ? { textContent: name, getAttribute: () => null } : null,
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
    querySelector: key => key === '.trade_title' ? { textContent: name, getAttribute: () => null } : null,
    querySelectorAll: selector => selector === 'td.flex_box p.f_gray' ? [postedAt] : selector === '*' ? actions :
      selector.startsWith('button,') ? actions : []
  };
  return { row, actions, clicks };
}

function evaluate(rows, { number = id, name = title, click = false,
  pathname = '/myroom/buy/buy_regist.html', hostname = 'www.itemmania.com', siteReinsert,
  minQuantity = '', maxQuantity = '', server = '', tradeSide = '' } = {}) {
  const document = {
    querySelectorAll: selector => selector === 'tr' ? rows.map(x => x.row) :
      selector.includes('[data-tid]') ? rows.map(x => x.row) : []
  };
  const scope = { document, location: { protocol: 'https:', hostname, pathname },
    getComputedStyle: () => ({ visibility: 'visible' }) };
  if (siteReinsert) scope.window = { reInsert: siteReinsert };
  const js = embedded.replace('__OWNED_TITLE_IDENTITY__', identityScript)
    .replace('__ARG__', JSON.stringify({ number, title: name, click,
      minQuantity, maxQuantity, server, tradeSide }));
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
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: '사이트에서 변경된 제목' } : null;
  const evidence = evaluate(rows);
  assert.equal(evidence.numberRows, 1);
  assert.equal(evidence.titleRows, 0);
  assert.equal(evidence.scannedRows, 1);
  const result = evaluate(rows, { click: true });
  assert.equal(result.status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS trusted own-row ID reposts even when the displayed title changes');
}
{
  const core = '10레벨 겁화 보석 구매합니다';
  const rows = [makeRealMarkupRow(id, '다른 표시 영역 / 제목: ' + core)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: core } : null;
  assert.equal(evaluate(rows).status, 'ready');
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS quantity range prefix in saved title does not cause false mismatch');
}
{
  const core = '10레벨 겁화작열 삽니다 24시 빠른거래';
  const rendered = '1 ~ 9,995 ' + core;
  const rows = [makeRealMarkupRow(id, rendered)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: rendered, getAttribute: () => null } : null;
  assert.equal(evaluate(rows, { name: core }).status, 'ready');
  assert.equal(evaluate(rows, { name: core, click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS unbracketed 1 ~ 9,995 quantity range is not mistaken for title');
}
{
  const core = '10레벨 겁화작열 삽니다 24시 빠른거래';
  const rows = [makeRealMarkupRow(id, '1 ~ 9,995 레벨 겁화작열 삽니다 24시')];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: '1 ~ 9,995 레벨 겁화작열 삽니다 24시',
      getAttribute: key => key === 'title' ? core : null } : null;
  assert.equal(evaluate(rows, { name: core }).status, 'ready');
  assert.equal(evaluate(rows, { name: core, click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS full official title attribute over truncated site display');
}
{
  // A visually incomplete level must not block an independently bound ID.
  const core = '10레벨 겁화작열 삽니다 24시 빠른거래';
  const rows = [makeRealMarkupRow(id, '1 ~ 9,995 레벨 겁화작열 삽니다 24시')];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: '1 ~ 9,995 레벨 겁화작열 삽니다 24시', getAttribute: () => null } : null;
  const result = evaluate(rows, { name: core, click: true });
  assert.equal(result.status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS title is advisory, masked level does not block authoritative ID');
}
{
  // Real 22.2.92 failure: 10 at the BEGINNING of the saved title is hidden
  // by the exact site's split-quantity range. Require the original range in
  // the stored job and a character-for-character match of the remaining text.
  const saved = '10 레벨 작열 삽니다 24 시';
  const shown = '1 ~ 9,995 레벨 작열 삽니다 24 시';
  const rows = [makeRealMarkupRow(id, shown)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: shown, getAttribute: () => null } : null;
  const args = { name: saved, minQuantity: '1', maxQuantity: '9995' };
  assert.equal(evaluate(rows, args).status, 'ready');
  assert.equal(evaluate(rows, { ...args, click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS observed leading 10 masked by verified 1 ~ 9,995 range');
}
{
  const saved = '10 레벨 작열 삽니다 24 시';
  const shown = '1 ~ 9,995 레벨 작열 삽니다 24 시';
  const rows = [makeRealMarkupRow(id, shown)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: shown, getAttribute: () => null } : null;
  const base = { name: saved, click: true, minQuantity: '1', maxQuantity: '9995' };
  assert.equal(evaluate(rows, { ...base, maxQuantity: '9999' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...base, minQuantity: '' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...base, name: '10 레벨 겁화 삽니다 24 시' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...base, name: '7 레벨 작열 삽니다 24 시' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...base, number: other }).status, 'not-found');
  assert.deepEqual(rows[0].clicks, ['repost','repost','repost','repost']);
  console.log('PASS title-embedded numeric range is advisory without independent quantity field');
}
{
  const shown = '1 ~ 9,995 레벨 작열 삽니다 24 시';
  const rows = [makeRealMarkupRow(id, shown)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: shown,
      getAttribute: key => key === 'title' ? '9 레벨 작열 삽니다 24 시' : null } : null;
  assert.equal(evaluate(rows, { name: '10 레벨 작열 삽니다 24 시', click: true,
    minQuantity: '1', maxQuantity: '9995' }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS conflicting original title attribute remains advisory');
}
{
  // User's B VPN v22.2.93 failure, where only the first of two level 10
  // tokens is masked by the exact verified split quantity range.
  const saved = '10 10 레벨 겁작 구매합니다';
  const shown = '1 ~ 999 10 레벨 겁작 구매합니다';
  const rows = [makeRealMarkupRow(id, shown)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: shown, getAttribute: () => null } : null;
  const args = { name: saved, minQuantity: '1', maxQuantity: '999' };
  assert.equal(evaluate(rows, args).status, 'ready');
  assert.equal(evaluate(rows, { ...args, click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS verified split range masks only the first of two leading level tokens');
}
{
  const saved = '10 10 레벨 겁작 구매합니다';
  const shown = '1 ~ 999 10 레벨 겁작 구매합니다';
  const rows = [makeRealMarkupRow(id, shown)];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: shown, getAttribute: () => null } : null;
  const exact = { name: saved, click: true, minQuantity: '1', maxQuantity: '999' };
  assert.equal(evaluate(rows, { ...exact, maxQuantity: '9995' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...exact, name: '10 10 레벨 작열 구매합니다' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...exact, name: '10 9 레벨 겁작 구매합니다' }).status, 'clicked');
  assert.equal(evaluate(rows, { ...exact, number: other }).status, 'not-found');
  assert.deepEqual(rows[0].clicks, ['repost','repost','repost']);
  console.log('PASS repeated leading levels and display-only ranges are advisory');
}
{
  const saved = '10 10 레벨 겁작 구매합니다';
  const shown = '1 ~ 999 10 레벨 겁작 구매합니다';
  const item = makeRealMarkupRow(id, shown);
  const originalQuery = item.row.querySelector.bind(item.row);
  item.row.querySelector = selector => selector === '.sub_trade_title'
    ? { textContent: '[1개~999개]' } : originalQuery(selector);
  const originalAll = item.row.querySelectorAll.bind(item.row);
  item.row.querySelectorAll = selector => selector === 'a[href]' ? [{
    getAttribute: attribute => attribute === 'href'
      ? '/buy/list_search.html?search_server_text=%EC%B9%B4%EB%A7%88%EC%9D%B8' : null
  }] : originalAll(selector);
  const valid = { name: saved, minQuantity: '1', maxQuantity: '999',
    server: '카마인', tradeSide: '구매', click: true };
  assert.equal(evaluate([item], valid).status, 'clicked');
  assert.equal(evaluate([item], { ...valid, server: '아만' }).status, 'mismatch');
  assert.equal(evaluate([item], { ...valid, tradeSide: '판매' }).status, 'mismatch');
  assert.equal(evaluate([item], { ...valid, maxQuantity: '9995' }).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost','repost']);
  const conflicting = item.row.querySelector;
  item.row.querySelector = selector => selector === '.sub_trade_title'
    ? { textContent: '[1개~9,995개]' } : conflicting(selector);
  assert.equal(evaluate([item], valid).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost','repost','repost']);
  assert.equal(evaluate([item], { ...valid, number: other }).status, 'not-found');
  assert.deepEqual(item.clicks, ['repost','repost','repost']);
  console.log('PASS ID-bound action survives stale saved quantity; server/side and wrong ID stay blocked');
}
{
  // Reported 11-20 failure: an existing listing's site range differs from
  // the saved range. The exact native reInsert ID still names ONE own item.
  const item = makeRealMarkupRow(id, '사이트 표시 제목이 변경된 게시글');
  const nativeQuery = item.row.querySelector.bind(item.row);
  item.row.querySelector = key => key === '.sub_trade_title'
    ? {textContent: '[1개~9,995개]'} : nativeQuery(key);
  assert.equal(evaluate([item], { name: '이전 저장 제목', minQuantity: '1',
    maxQuantity: '9999', click: true }).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost']);
  // A row with no ID-bound action must NEVER be rescued by matching quantity.
  const spoof = makeRealMarkupRow(id, '사이트 표시 제목이 변경된 게시글', other);
  spoof.row.querySelector = key => key === '.sub_trade_title'
    ? {textContent: '[1개~9,995개]'} : null;
  assert.notEqual(evaluate([spoof], {minQuantity: '1',maxQuantity: '9999',
    click: true }).status, 'clicked');
  assert.deepEqual(spoof.clicks, []);
  console.log('PASS user-reported stale split range does not authorize an unbound repost');
}
{
  const item = makeRealMarkupRow(id, '다른 사이트 원본 제목');
  item.row.innerText = `정확한 제목: ${title} · ${id}`;
  assert.equal(evaluate([item], { click: true }).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost']);
  console.log('PASS row title mismatch cannot override authoritative ID-bound action');
}
{
  // Real 11-20 alert: the site rewrites spacing/punctuation while the
  // authenticated repost control still carries the exact 16-digit ID.
  const stored = '+ 10 + 레벨 겁화 삽니다 + 즉시대기중 +';
  const item = makeRealMarkupRow(id, '+ 10 + 레벨 겁화 삽니다+ 즉시대기중 +');
  assert.equal(evaluate([item], { name: stored, click: true }).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost']);
  console.log('PASS user-reported formatting mismatch no longer blocks repost');
}
{
  const item = makeRealMarkupRow(id, '제목이 없는 원본 글');
  item.row.querySelector = () => null;
  assert.equal(evaluate([item], { click: true }).status, 'clicked');
  assert.deepEqual(item.clicks, ['repost']);
  console.log('PASS absent title element does not block an exact native repost ID');
}
{
  // A random row containing the number in text is NOT a bound listing.
  const item = makeRow(id, title);
  item.row.getAttribute = () => null;
  item.row.innerText = `${id} ${title}`;
  assert.equal(evaluate([item], { click: true }).status, 'error');
  assert.deepEqual(item.clicks, []);
  console.log('PASS text-only ID cannot authorize an unbound repost control');
}
{
  const rows = [makeRealMarkupRow(id, '10레벨 작열 보석 구매합니다')];
  rows[0].row.querySelector = selector => selector === '.trade_title'
    ? { textContent: '10레벨 작열 보석 구매합니다' } : null;
  assert.equal(evaluate(rows, { click: true }).status, 'clicked');
  assert.deepEqual(rows[0].clicks, ['repost']);
  console.log('PASS genuine title changes are advisory for ID-bound reposts');
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
