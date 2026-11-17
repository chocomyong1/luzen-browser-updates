const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');

const src = fs.readFileSync(__dirname + '/../AppSource/MyListingsImport.cs', 'utf8');
const importer = fs.readFileSync(__dirname + '/../AppSource/AutomationForm.MyListings.cs', 'utf8');
// Navigation completion from another operation must never be mistaken for the
// signed-in management page we actually requested. Read-only retry is allowed.
assert.match(importer, /e\.NavigationId == targetId\.Value/);
assert.match(importer, /_web\.NavigationStarting \+= starting/);
assert.match(importer, /_web\.NavigationCompleted \+= completed/);
assert.match(importer, /for \(int attempt = 1; attempt <= 2; attempt\+\+\)/);
assert.match(importer, /scan\.NavigationFailures > 0/);
const match = src.match(/public const string ExtractPageScript = """\s*\n([\s\S]*?)\n\s*""";/);
assert.ok(match, 'Missing embedded import extractor');

const url = 'https://www.itemmania.com/myroom/buy/buy_regist.html';
const fakeEl = (tagName, value, attrs = {}, children = []) => ({
  tagName, textContent: value, innerText: value, children,
  getAttribute: name => attrs[name] ?? null,
  querySelectorAll: () => [], querySelector: () => null,
});

function listing(number, max, price) {
  const categoryLink = fakeEl('A', '로스트아크 카마인', {
    href: `/buy/list_search.html?search_game_text=${encodeURIComponent('로스트아크')}&search_server_text=${encodeURIComponent('카마인')}`,
  });
  const detail = fakeEl('A', '상세 보기', {href: `buy_regist_view.html?id=${number}&page=`});
  const edit = fakeEl('A', '수정', {href: `buy_regist_edit.html?id=${number}&page=`});
  const repost = fakeEl('A', '', {onclick: `reInsert('${number}','2696');`});
  const range = fakeEl('SPAN', `[1개~${max.toLocaleString('en-US')}개]`);
  const title = fakeEl('DIV', '10레벨 겁화 보석 구매합니다');
  const span = fakeEl('SPAN', '1당');
  const cells = [
    fakeEl('TD', '로스트아크 카마인', {}, [categoryLink]),
    fakeEl('TD', '아이템'),
    fakeEl('TD', `${range.textContent} ${title.textContent}`, {}, [detail,edit,range,title]),
    fakeEl('TD', `1당 ${price.toLocaleString('en-US')}원`, {}, [span]),
    fakeEl('TD', '11-16 10:53', {}, [repost]),
  ];
  cells[2].querySelector = selector => selector === '.sub_trade_title' ? range :
    selector === '.trade_title' ? title : null;
  cells[3].querySelector = selector => selector.includes('span') ? span : null;
  const row = fakeEl('TR', cells.map(x => x.textContent).join(' '), {}, cells);
  row.querySelector = selector => selector === 'td' ? cells[0] : null;
  row.querySelectorAll = selector => {
    if (selector === '*') return [...cells, categoryLink, detail, edit, range, title, span, repost];
    if (selector === 'a[href]') return [categoryLink, detail, edit];
    if (selector === 'a[href],a[onclick],button[onclick],input[name]') return [categoryLink, detail, edit, repost];
    if (selector === 'a,button,[data-url],[data-href]') return [categoryLink, detail, edit, repost];
    if (selector === '[class],[id]' || selector.startsWith('[data-')) return [];
    return [];
  };
  return row;
}

const rows = [
  listing('2026070701992028', 999, 34100),
  listing('2026070701991860', 9995, 29900),
];
const table = {className:'g_green_table tb_list buy_list',querySelectorAll:s => s==='tr'?rows:[]};
const doc = {
  title:'내 구매 등록물품', body:{innerText:'구매 등록 물품 목록'},
  querySelectorAll: selector => selector==='table'?[table]:[],
  querySelector: () => null,
};
const location = new URL(url);
const result = vm.runInNewContext(match[1],{document:doc,location,URL},{timeout:5000});
const items = JSON.parse(JSON.stringify(result.listings));
assert.equal(result.error, '',result.error);
assert.equal(items.length, 2, 'First row in a headerless table must NOT be dropped');
assert.equal(items[0].listingNumber,'2026070701992028');
assert.equal(items[0].itemCategory,'아이템');
assert.equal(items[0].tradingMethod,'분할');
assert.equal(items[0].unit,'개');
assert.equal(items[0].minQuantity,'1');
assert.equal(items[0].maxQuantity,'999');
assert.equal(items[1].maxQuantity,'9995');
assert.equal(items[0].price,'34,100');
assert.equal(items[0].game,'로스트아크');
assert.equal(items[0].server,'카마인');
assert.equal(items[0].title,'10레벨 겁화 보석 구매합니다');
assert.ok(items[0].listingUrl.includes('id=2026070701992028'));
assert.ok(items[0].listingUrl.includes('buy_regist_view.html'), 'An exact View link wins over a different Edit link for same ID');
assert.ok(items[0].editUrl.includes('buy_regist_edit.html'), 'Keep the exact own-row Edit URL for read-only detail enrichment');
assert.ok(items[0].editUrl.includes('id=2026070701992028'), 'Edit link must point to the same verified listing');
assert.equal(items[0].quantity, '', 'The per-unit label must NOT be mistaken for total stock');
console.log('PASS ItemMania imported rows: headerless first row, two ranges, price, category, links');
