const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..', 'AppSource');
const form = fs.readFileSync(path.join(root, 'AutomationForm.cs'), 'utf8');
const panel = fs.readFileSync(path.join(root, 'AutomationForm.MarketPrices.cs'), 'utf8');
const parser = fs.readFileSync(path.join(root, 'MarketPriceParser.cs'), 'utf8');

assert.match(form, /ComboBox _subId = new\(\) \{ DropDownStyle = ComboBoxStyle\.DropDown \}/);
assert.match(form, /Field\("즉시구매", _instantTransaction/);
assert.match(form, /Field\("실시간 시세", BuildMarketPricePanel\(\)/);
assert.match(form, /Field\("매크로", _macro/);
assert.match(form, /Field\("캐릭터명", _character/);
assert.doesNotMatch(form, /Field\("캐릭터정보"|Field\("캐릭터 정보"/);
assert.match(panel, /Interval = 120_000/);
assert.match(panel, /MarketPriceParser\.Parse\(html\)/);
assert.match(panel, /_marketQuotes = new Dictionary<string, MarketPriceQuote>/);
assert.match(panel, /_marketPriceStatus\.Text = .*외부 참고/);
assert.match(parser, /"https:\/\/gemdex\.kr\/markets\?game=lost-ark"/);
assert.match(parser, /value \* 10/);
assert.match(parser, /if \(mania is null && bay is null\) continue/);
console.log('Item-registration fields and read-only market display guards OK');
