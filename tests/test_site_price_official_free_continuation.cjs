// Offline official-UI regression. No network, paid action or form POST.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const code = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const flow = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');
const observer = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceSubmitObserver.cs'), 'utf8');

function script(name) {
  const match = code.match(new RegExp(`public const string ${name} = """\\s*([\\s\\S]*?)\\s*""";`));
  assert.ok(match, `missing ${name}`);
  return match[1];
}

function page({name='무료 등록', count=1, amount='0', penalty='1',
  pathname='/myroom/buy/buy_re_reg.html', contactApproved=true}={}) {
  const layer={
    isConnected:true,disabled:false,
    innerText:`프리미엄 등록안내 총 결제금액: ${amount}원`,
    getBoundingClientRect:()=>({left:5,right:350,top:5,bottom:350,
      width:345,height:345}),
    contains:()=>true
  };
  const buttons=Array.from({length:count},()=>({
    isConnected:true,disabled:false,innerText:name,
    getBoundingClientRect:()=>({left:50,right:150,top:100,bottom:140,
      width:100,height:40}),
    closest:()=>layer,contains:()=>false,scrollIntoView:()=>{}
  }));
  const form={contains:()=>false};
  const p={clickSeen:true,submitSeen:true,canType:()=>true,
    titleChanged:()=>false,guardBlocked:false,
    form,desired:'71000',priceInput:{value:'71000'},money:s=>String(s).replace(/\D/g,'')};
  const document={
    querySelectorAll:()=>buttons,
    getElementById:()=>({value:penalty}),
    elementFromPoint:()=>buttons[0]||null
  };
  const ctx={window:{__luzenPriceNativeProbe:p},document,
    location:{protocol:'https:',hostname:'www.itemmania.com',pathname},
    getComputedStyle:()=>({visibility:'visible',display:'block',pointerEvents:'auto'}),
    innerWidth:700,innerHeight:500};
  assert.equal(typeof contactApproved,'boolean');
  return {ctx,p,buttons,layer};
}

function find(page) {return vm.runInNewContext(script('FindOfficialFreeContinuationScript'),page.ctx);}

{
  const site=page();
  const result=find(site);
  assert.equal(result.status,'free-ready');
  assert.equal(result.x,100);
  assert.equal(result.y,120);
  assert.equal(vm.runInNewContext(script('VerifyOfficialFreeContinuationScript'),site.ctx),true);
  site.buttons[0].innerText='유료 결제';
  assert.equal(vm.runInNewContext(script('VerifyOfficialFreeContinuationScript'),site.ctx),false);
}

assert.equal(find(page({name:'프리미엄등록'})).status,'absent',
  'Never choose paid registration');
assert.equal(find(page({name:'확인'})).status,'absent',
  'Never choose an ambiguous generic confirmation');
assert.equal(find(page({amount:'1000'})).status,'paid',
  'Never authorize a non-zero payment');
assert.equal(find(page({count:2})).status,'ambiguous',
  'Never click one of multiple free buttons');
assert.equal(find(page({penalty:'0'})).status,'not-applicable');
assert.equal(find(page({pathname:'/myroom/buy/buy_regist.html'})).status,'blocked');
{
  const site=page();site.p.priceInput.value='72000';
  assert.equal(find(site).status,'blocked', 'Original price/desired price mismatch');
}
{
  const site=page();site.p.clickSeen=false;
  assert.equal(find(site).status,'blocked', 'Original submit must occur first');
}
{
  const site=page();site.p.canType=()=>false;
  assert.equal(find(site).status,'blocked', 'Exact ID/title safety must remain');
}
assert.match(flow,/if \(_unattendedSitePriceEdit\)\s*await TryAdvanceOfficialFreeRegistrationAsync/);
assert.match(flow,/var observed = await observing/);
assert.match(flow,/VerifyOfficialFreeContinuationScript/);
assert.match(flow,/observer\.ContactApproved/);
assert.match(flow,/observer\.CategoryCheckStarted/);
assert.match(observer,/ContactApproved => _contactDecision\.Task\.IsCompletedSuccessfully/);
const postFree = code.slice(code.indexOf('public const string FindOfficialFreeContinuationScript'),
  code.indexOf('public const string NativePriceButtonClickedScript'));
assert.doesNotMatch(postFree,/\.submit\(\)|\bfetch\(|new FormData\(/);
console.log('PASS first-party accepted-contact-only free premium UI, one guarded browser click, no paid/duplicate POST');
