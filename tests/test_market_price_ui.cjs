const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..', 'AppSource');
const form = fs.readFileSync(path.join(root, 'AutomationForm.cs'), 'utf8');
assert.doesNotMatch(form, /Field\("서브 ID"/);
assert.doesNotMatch(form, /Field\("즉시구매"/);
assert.doesNotMatch(form, /Field\("실시간 시세"/);
assert.doesNotMatch(form, /Field\("매크로"/);
assert.match(form, /Field\("캐릭터명", _character/);
assert.doesNotMatch(form, /Field\("캐릭터정보"|Field\("캐릭터 정보"/);
assert.equal(fs.existsSync(path.join(root, 'AutomationForm.MarketPrices.cs')), false);
assert.equal(fs.existsSync(path.join(root, 'MarketPriceParser.cs')), false);
console.log('Unwanted editor fields removed; character name retained');
