'use strict';
// Offline test: no authenticated browser and no real site POST. The probes
// must reveal only safe branch indicators without altering the site flow.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const site = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const flow = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const observer = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceSubmitObserver.cs'), 'utf8');
const get = name => {
  const match = site.match(new RegExp(`public const string ${name} = """([\\s\\S]*?)""";`));
  assert(match, `Missing ${name}`);
  new vm.Script(match[1]);
  return match[1];
};
const install = get('InstallSiteSubmitErrorProbeScript');
const inspect = get('PostContactBlockerProbeScript');
const handlers = new Map();
const window = {
  addEventListener: (type, handler) => {
    assert(!handlers.has(type), `Unexpected handler replacement: ${type}`);
    handlers.set(type, handler);
  },
  premiumLayerOpen() { return false; }
};
const document = {
  getElementById: id => id === 'user_premium_penalty' ? {value:'1'} : null,
  querySelectorAll: () => [{getBoundingClientRect: () => ({width:300,height:220})}]
};
const context = {
  window,document,getComputedStyle: () => ({visibility:'visible',display:'block'}),
  location: {protocol:'https:',hostname:'www.itemmania.com',
    pathname:'/myroom/buy/buy_re_reg.html'}
};
assert.equal(vm.runInNewContext(install,context),true);
assert.equal(vm.runInNewContext(install,context),true,'Install once only');
assert.deepEqual([...handlers.keys()],['error','unhandledrejection']);
handlers.get('error')({message:'Potential user details: NEVER print this'});
handlers.get('unhandledrejection')({reason:'NEVER print this'});
const result = vm.runInNewContext(inspect,context);
assert.match(result,/추가 확인 분기 표시:적용/);
assert.match(result,/보이는 대화 레이어:1/);
assert.match(result,/사이트 스크립트 예외:1/);
assert.match(result,/비동기 예외:1/);
assert.doesNotMatch(result,/Potential user details|NEVER print this|user_contact|user_mobile/);
context.location.hostname='example.com';
assert.equal(vm.runInNewContext(inspect,context),'공식 수정 화면 아님');

// The first-party contact check can finish with HTTP 200 while rejecting
// res.split('|')[0] != 'S'. Never invent a second POST on that rejection.
assert.match(observer,/Network\.getResponseBody/);
assert.match(observer,/"S" => new ContactDecision\(true/);
assert.match(observer,/new ContactDecision\(false/);
assert.match(observer,/if \(_contactDecision\.Task\.Result\.Approved == false && _requestId\.Length == 0\)/);
assert.match(observer,/ContactCheckRejected = true/);
assert.match(flow,/await submissionObserver\.StartAsync\(\);[\s\S]*?InstallSiteSubmitErrorProbeScript/);
assert.match(flow,/SitePriceEdit\.PostContactBlockerProbeScript/);
assert.match(flow,/사이트 후속 분기: /);
assert.match(flow,/observed\.ContactCheckRejected && !observed\.RequestObserved/);
assert.match(flow,/_sitePriceLastReadback\?\.OriginalUnchangedNoReplacement == true && !previousReview/);
assert.match(flow,/job\.RequiresReview = false;[\s\S]*?job\.Enabled = false;[\s\S]*?job\.NextDueUtc = DateTime\.MaxValue/);
assert.match(flow,/job\.PendingSitePriceTarget = previousPendingPrice;/);
assert.doesNotMatch(install+inspect,/\.submit\(|fetch\(|XMLHttpRequest\(|\.click\(|\.value\s*=/);
console.log('PASS site contact approval branch, passive premium/JS probes and no retry');
