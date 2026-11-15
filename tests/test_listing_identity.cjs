const fs = require('fs');
const vm = require('vm');
const assert = require('assert');
const cs = fs.readFileSync(__dirname + '/../AppSource/ListingIdentity.cs', 'utf8');
const match = cs.match(/public const string BrowserScript = """\s*\n([\s\S]*?)\n\s*""";/);
assert(match, 'Cannot extract BrowserScript');

const target = '2026070507572345';
const other = '2026070507572346';
const title = '[1개~9,995개] 10레벨 겁화 보석 구매합니다';
const detail = `https://www.itemmania.com/myroom/buy/detail/${target}`;

function inspect({url=detail, entryUrl=detail, number=target, body=title,
                  attrs={}, links=[], hidden=[]} = {}) {
  const loc = new URL(url);
  const document = {
    title: '',
    body: {innerText: body},
    querySelectorAll(selector) {
      if (selector === 'a[href]') return links.map(href => ({href}));
      if (selector === 'input[type="hidden"][name]') return hidden;
      if (selector === '[data-tid],[data-listing-id]') return [];
      const a = selector.match(/^\[(data-[^\]]+)\]$/);
      if (a) return (attrs[a[1]] || []).map(value => ({getAttribute: () => value}));
      return [];
    }
  };
  const arg = {title, number, entryUrl};
  const script = match[1].replace('__ARG__', JSON.stringify(arg));
  const result = vm.runInNewContext(script, {URL, location:loc, document}, {timeout:3000});
  return JSON.parse(JSON.stringify(result));
}

const ok = x => x.number === target && x.titlePresent === true &&
  x.detailPage === true && x.error === '';
assert(ok(inspect()), 'numeric detail path should confirm');
assert(ok(inspect({url:`https://www.itemmania.com/myroom/buy/detail?trade_id=${target}`,
  entryUrl:`https://www.itemmania.com/myroom/buy/detail?trade_id=${target}`})), 'query ID should confirm');
assert(ok(inspect({url:`https://www.itemmania.com/myroom/buy/detail?product=${target}`,
  entryUrl:`https://www.itemmania.com/myroom/buy/detail?product=${target}`})), 'exact imported URL should confirm');
assert(!ok(inspect({url:`https://www.itemmania.com/myroom/buy/detail/${other}`})), 'other detail page must not confirm');
assert(!ok(inspect({body:'다른 등록글 페이지'})), 'different title must not confirm');
assert(!ok(inspect({body:title+' 등록번호: '+other})), 'contradictory labeled number must block');
assert(!ok(inspect({url:`https://www.itemmania.com/myroom/buy/buy_regist.html?trade_id=${target}`,
  entryUrl:`https://www.itemmania.com/myroom/buy/buy_regist.html?trade_id=${target}`})),
  'own-list page must not impersonate a detail page');
assert(!inspect({entryUrl:'', number:''}).error.includes('읽기 오류'), 'new item should not throw');
console.log('Listing identity JavaScript fixtures passed: 8');
