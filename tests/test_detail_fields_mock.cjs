// Offline fixture for characters/details on verified ItemMania detail pages.
// No logged-in pages or real user text are needed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(__dirname + '/../AppSource/MyListingsImport.cs', 'utf8');
const raw = source.match(/public const string ExtractDetailScript = """([\s\S]*?)""";/)?.[1];
assert(raw);
const id = '2026070507571234';

function extract(path, labels, fields = []) {
  const labelNodes = labels.map(([label, value]) => ({
    textContent: label,
    getAttribute: () => null,
    nextElementSibling: { textContent: value }
  }));
  const inputs = fields.map(([name, value]) => ({
    tagName: 'TEXTAREA', id: name, value,
    getAttribute: key => key === 'name' ? name : null
  }));
  const document = {
    querySelectorAll: selector => selector === '[data-tid],[data-listing-id],[data-goods-id]' ? [] :
      selector === '[data-label],dt,th,label,strong,b,span' ? labelNodes :
      selector === 'input,select,textarea' ? inputs : [],
    getElementById: () => null
  };
  const url = `https://trade.itemmania.com/item/detail?tid=${path}`;
  const u = new URL(url);
  const result = vm.runInNewContext(raw.replace('__EXPECTED_NUMBER__', JSON.stringify(id)), {
    document, location: { href: url, protocol: u.protocol, hostname: u.hostname,
      pathname: u.pathname, search: u.search }, URL
  }, { timeout: 1000 });
  return JSON.parse(JSON.stringify(result));
}

const fromLabels = extract(id, [['캐릭터정보', '내캐릭터'], ['상세내용', '10레벨 보석 구매']]);
assert.equal(fromLabels.character, '내캐릭터');
assert.equal(fromLabels.description, '10레벨 보석 구매');
const fromForm = extract(id, [], [['characterName', '내캐릭터'], ['goodsContent', '보석 거래 상세']]);
assert.equal(fromForm.character, '내캐릭터');
assert.equal(fromForm.description, '보석 거래 상세');
const mismatched = extract('20260705075712345', [['캐릭터명', '타인 캐릭터']]);
assert.equal(mismatched.listingNumber, '');
assert.equal(mismatched.character, '');
console.log('PASS detail character/description labels and form aliases, strict listing identity');
