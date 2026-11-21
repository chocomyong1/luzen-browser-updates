'use strict';

// Offline regression for real-form diagnostic inspection. This test does NOT
// claim that ItemMania's actual submit handler has been identified or fixed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..');
const source = fs.readFileSync(path.join(root, 'AppSource/SitePriceEdit.cs'), 'utf8');
const flow = fs.readFileSync(path.join(root, 'AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const menu = fs.readFileSync(path.join(root, 'AppSource/AutomationForm.cs'), 'utf8');
const scriptMatch = source.match(/public const string SubmitBindingsInspectionScript = """([\s\S]*?)""";/);
assert(scriptMatch, 'Read-only submit inspection script must exist');
const number = '2026112100145715';
const script = scriptMatch[1].replace('__ARG__', JSON.stringify({number}));
new vm.Script(script);

function inspect({hostname = 'www.itemmania.com', hiddenId = number, hasButton = true} = {}) {
  const changes = [];
  const field = (attrs, value) => ({
    ...attrs,
    get value() { return value; },
    set value(next) { changes.push(next); throw new Error('read-only script changed form value'); }
  });
  const id = field({type: 'hidden'}, hiddenId);
  const price = field({name: 'user_division_price'}, 'TOP_SECRET_PRICE_90000');
  const title = field({name: 'user_title'}, 'TOP_SECRET_TITLE');
  const form = {
    method: 'post', enctype: 'application/x-www-form-urlencoded',
    action: 'https://www.itemmania.com/myroom/buy/buy_re_reg_ok.php?TOP_SECRET_TOKEN=abc',
    onsubmit: function siteValidationHandler() { return false; },
    getAttribute: name => name === 'onsubmit' ? 'return validatePriceAndSubmit();' : null,
    querySelector: selector => {
      if (selector === 'button#ok_btn.buy_re_reg[type="submit"]') return hasButton ? button : null;
      if (selector === 'input[name="user_division_price"]') return price;
      if (selector === 'input[name="user_title"],textarea[name="user_title"]') return title;
      return null;
    },
    querySelectorAll: selector => selector === 'input[type="hidden"]' ? [id] : []
  };
  const button = {
    disabled: false,
    onclick: function submitWithSiteChecks() { return false; },
    getAttribute: name => name === 'onclick' ? 'if (!checkReady()) return false; return triggerSubmit();' : null,
    click() { throw new Error('Read-only inspection must not click'); }
  };
  form.submit = () => { throw new Error('Read-only inspection must not submit'); };
  const location = {
    protocol: 'https:', hostname, pathname: '/myroom/buy/buy_re_reg.html',
    href: `https://${hostname}/myroom/buy/buy_re_reg.html?TOP_SECRET_QUERY=abc`
  };
  const document = {
    querySelector: selector => selector === 'form#frmBuy' ? form : null,
    querySelectorAll: selector => selector === 'script[src]' ? [
      {src: `https://${hostname}/js/buy_re_reg.js?TOP_SECRET_ASSET_TOKEN=def`},
      {src: 'https://thirdparty.invalid/private-token.js'}
    ] : []
  };
  const window = {
    jQuery: Object.assign(function jQuery() {}, {_data: (target, type) => {
      assert.equal(type, 'events');
      return target === form ? {submit: [{handler: function nativeSiteSubmitGuard() { return false; }}]} : {};
    }})
  };
  const result = vm.runInNewContext(script, {
    location, document, window, URL,
    fetch: () => { throw new Error('Read-only inspection must not fetch'); }
  });
  assert.deepEqual(changes, [], 'Read-only diagnostic must not change any form values');
  return result;
}

const valid = inspect();
assert.equal(valid.status, 'inspected');
assert.match(valid.message, /폼 action 경로: \/myroom\/buy\/buy_re_reg_ok\.php/);
assert.match(valid.message, /onsubmit 함수: 함수=siteValidationHandler/);
assert.match(valid.message, /jQuery 폼 submit.*nativeSiteSubmitGuard/);
assert.match(valid.message, /관련 사이트 스크립트 파일명: buy_re_reg.js/);
assert.match(valid.message, /버튼 onclick.*submitWithSiteChecks/);
assert.match(valid.message, /가격 입력·네트워크 요청을 실행하지 않았습니다/);
assert.doesNotMatch(valid.message, /TOP_SECRET|2026112100145715|90000|https?:\/\//,
  'Never copy a listing ID, price, title, query string, token, or full URL');
assert.equal(inspect({hostname:'fake-itemmania.com'}).status, 'error');
assert.equal(inspect({hiddenId:'2026112100000000'}).status, 'error');
assert.equal(inspect({hasButton:false}).status, 'error');

assert.match(menu, /사이트 가격 수정폼 분석·복사 \(제출 없음\)/);
assert.match(flow, /await ApplySitePriceAsync\(_selected, bulkApproval: false, diagnosticOnly: true\)/);
assert.match(flow, /if \(diagnosticOnly\)\s*\{\s*var inspection = SitePriceEdit\.Decode\(/);
const diagnosticStart = flow.indexOf('if (diagnosticOnly)\n            {\n                var inspection');
const actualPostObserver = flow.indexOf('using var submissionObserver = new SitePriceSubmitObserver');
assert(diagnosticStart > 0 && actualPostObserver > diagnosticStart,
  'Diagnostic must exit before setting up any real price edit submission');
assert.match(flow.slice(diagnosticStart, actualPostObserver), /return true;/);
assert.match(flow, /if \(!diagnosticOnly && \(job\.RequiresReview \|\| unresolvedPriceAttempt\)\)/);
assert.match(flow, /if \(!diagnosticOnly && \(MyListingsImport\.HasSuspiciousDuplicatedLevel/);
assert.match(flow, /if \(!bulkApproval && !diagnosticOnly\) SaveSelectedJob\(false\)/);
console.log('PASS read-only form inspection, privacy filtering, mismatch rejection, and pre-submission exit');
