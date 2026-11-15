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
function makeRow(number, name, actionName = '재등록') {
  const clicks = [];
  const button = {
    tagName: 'BUTTON', isConnected: true, disabled: false, innerText: actionName, textContent: actionName,
    value: '', title: '', getClientRects: () => [1], getAttribute: () => null,
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

function evaluate(rows, { number = id, name = title, click = false,
  pathname = '/myroom/buy/buy_regist.html', hostname = 'www.itemmania.com' } = {}) {
  const document = {
    querySelectorAll: selector => selector === 'tr' ? rows.map(x => x.row) :
      selector.includes('[data-tid]') ? rows.map(x => x.row) : []
  };
  const scope = { document, location: { protocol: 'https:', hostname, pathname },
    getComputedStyle: () => ({ visibility: 'visible' }) };
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
