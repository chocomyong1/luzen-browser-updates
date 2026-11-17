// Offline fixture for characters/details on verified ItemMania detail pages.
// No logged-in pages or real user text are needed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(__dirname + '/../AppSource/MyListingsImport.cs', 'utf8');
const raw = source.match(/public const string ExtractDetailScript = """([\s\S]*?)""";/)?.[1];
assert(raw);
const id = '2026070507571234';

function extract(path, labels, fields = [], inFrame = false, visibleText = '') {
  const labelNodes = labels.map(([label, value]) => ({
    textContent: label,
    getAttribute: () => null,
    nextElementSibling: { textContent: value }
  }));
  const inputs = fields.map(([name, value]) => ({
    tagName: 'TEXTAREA', id: name, value,
    getAttribute: key => key === 'name' ? name : null
  }));
  const frameDocument = {
    body: {innerText: visibleText},
    querySelectorAll: selector => selector === '[data-tid],[data-listing-id],[data-goods-id]' ? [] :
      selector === '[data-label],dt,th,label,strong,b,span' ? labelNodes :
      selector === 'input,select,textarea' ? inputs : [],
    getElementById: () => null
  };
  const document = inFrame ? {
    querySelectorAll: selector => selector === 'iframe,frame' ? [{contentDocument: frameDocument}] : [],
    getElementById: () => null
  } : frameDocument;
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
const fromSameOriginFrame = extract(id, [], [['character_name', '프레임캐릭터'], ['goods_content', '프레임 상세설명']], true);
assert.equal(fromSameOriginFrame.character, '프레임캐릭터');
assert.equal(fromSameOriginFrame.description, '프레임 상세설명');
const screenshotVisible = extract(id, [], [], false,
  '구매등록 물품\n물품정보\n[1개~9,995개] 10레벨 보석 구매합니다\n구매자 캐릭터명 : 푸닉스\n내 거래정보\n거래 진행 상황\n상세설명\n상시대기중입니다\n');
assert.equal(screenshotVisible.character, '푸닉스', 'Visible own-detail buyer character must import');
assert.equal(screenshotVisible.description, '상시대기중입니다', 'Visible own-detail description must import');
const perUnit = extract(id, [
  ['판매금액', '1당 80,000원'],
  ['수량', '[1개~9,995개]'],
  ['거래단위', '개']
]);
assert.equal(perUnit.price, '80,000', 'Per-unit label must not become a 1-won price');
assert.equal(perUnit.quantity, '', 'Quantity range must not become total quantity 1');
assert.equal(perUnit.unit, '개');
const mismatched = extract('20260705075712345', [['캐릭터명', '타인 캐릭터']]);
assert.equal(mismatched.listingNumber, '');
assert.equal(mismatched.character, '');
console.log('PASS detail character/description labels and form aliases, strict listing identity');
