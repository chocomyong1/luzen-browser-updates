'use strict';
// A static-script GET-only diagnostic for the real site's canceled onsubmit.
// No session, account, price or live ItemMania request is used in this test.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const src=fs.readFileSync(path.join(__dirname,'../AppSource/SitePriceEdit.cs'),'utf8');
const flow=fs.readFileSync(path.join(__dirname,'../AppSource/AutomationForm.SitePriceEdit.cs'),'utf8');
const main=fs.readFileSync(path.join(__dirname,'../AppSource/MainForm.cs'),'utf8');
const automation=fs.readFileSync(path.join(__dirname,'../AppSource/AutomationForm.cs'),'utf8');
const script=src.match(/public const string SubmitValidatorSourceScript = """([\s\S]*?)""";/)?.[1];
assert(script,'Missing read-only site script analyzer');
new vm.Script(script.replace('__ARG__',JSON.stringify({number:'2026112100145715'})));
const listing='2026112100145715';
async function inspect({host='www.itemmania.com',number=listing,hasButton=true}={}){
  const requested=[];
  const form={
    querySelector:s=>s==='button#ok_btn.buy_re_reg[type="submit"]'&&hasButton?{}:
      s==='input[name="user_division_price"]'?{}:null,
    querySelectorAll:s=>s==='input[type="hidden"]'?[{value:number}]:[],
    submit(){throw Error('Must not submit');}
  };
  const document={querySelector:s=>s==='form#frmBuy'?form:null,
    querySelectorAll:s=>s==='script[src]'?[
      {src:`https://${host}/myroom/buy/js/buy_re_reg.js?PRIVATE_TOKEN=abcdef`},
      {src:`https://${host}/_js/editor.js?PRIVATE_VALUE=xyz`},
      {src:`https://${host}/buy/js/paid_service.js`},
      {src:'https://cdn.itemmania.com/scripts/buy_re_reg.js?SENSITIVE=yes'},
      {src:'https://evil.invalid/js/buy_re_reg.js'}]:[]};
  const location={protocol:'https:',hostname:host,pathname:'/myroom/buy/buy_re_reg.html',
    href:`https://${host}/myroom/buy/buy_re_reg.html?PRIVATE_QUERY=foo`,origin:`https://${host}`};
  const fetch=async (url,options)=>{
    requested.push([url,options]);
    assert.equal(options.method,'GET');
    assert.equal(options.redirect,'error');
    assert(!url.includes('?') && url.startsWith(`https://${host}/`));
    return {ok:true,status:200,headers:{get:s=>s==='content-type'?'application/javascript':null},
      text:async()=>"const private_value='TOP_SECRET_RUNTIME_VALUE';\n"+
        "document.querySelector('#frmBuy').onsubmit=function(){if(!validPrice())return false;}"};
  };
  const window={};
  const started=vm.runInNewContext(script.replace('__ARG__',JSON.stringify({number:listing})),
    {location,document,window,URL,fetch},{timeout:1000});
  // WebView2 serializes the direct script result. It must not be a pending
  // Promise, which previously showed up as a blank/`null` native response.
  assert.equal(typeof started?.then,'undefined','WebView2 receives a serializable object, not Promise');
  assert.notEqual(JSON.stringify(started),'{}');
  if(started.status==='started'){
    assert.equal(window.__vantaPriceValidatorInspection.status,'loading');
    await new Promise(setImmediate);
  }
  return {result:started.status==='started'?window.__vantaPriceValidatorInspection:started,
    started,requested};
}
(async()=>{
  const {result,started,requested}=await inspect();
  assert.equal(started.status,'started');
  assert.equal(result.status,'inspected');
  assert.equal(requested.length,2,'GET only two official same-origin JS assets');
  assert.match(result.message,/buy_re_reg\.js/);
  assert.match(result.message,/editor\.js/);
  assert.match(result.message,/공식 CDN 경로 \(GET하지 않음\): cdn\.itemmania\.com\/scripts\/buy_re_reg\.js/);
  assert.match(result.message,/onsubmit/);
  assert.doesNotMatch(result.message,/TOP_SECRET|PRIVATE_|2026112100145715|evil\.invalid|https?:\/\//);
  assert.match(result.message,/사이트 제출·클릭·가격 입력은 실행하지 않았습니다/);
  assert.equal((await inspect({number:'2026112100000000'})).result.status,'error');
  assert.equal((await inspect({hasButton:false})).result.status,'error');
  assert.equal((await inspect({host:'fake-itemmania.com'})).result.status,'error');
  assert.match(flow,/BuildSubmitValidatorSourceScript\(job\.ListingNumber\)/);
  // The real top toolbar's 관리 menu must expose the diagnostic. A prior
  // release only wired it to the hidden edit-only extra-actions menu.
  assert.match(main,/diagnosticsMenu\.DropDownItems\.Add\("사이트 가격 수정폼 분석·복사 \(제출 없음\)"/);
  assert.match(main,/_module\.InspectSelectedPriceSubmitFromHeaderAsync\(\)/);
  assert.match(automation,/public Task InspectSelectedPriceSubmitFromHeaderAsync\(\) => InspectSelectedPriceSubmitBindingsAsync\(\);/);
  assert.match(flow,/SitePriceEdit\.SubmitValidatorSourceStatusScript/);
  assert.match(flow,/DateTime\.UtcNow\.AddSeconds\(12\)/);
  assert.match(flow,/브라우저 스크립트가 결과를 반환하지 않았습니다/);
  assert.match(flow,/Clipboard\.SetText\(inspection\.Message \+ "\\n\\n" \+ sourceNote\)/);
  assert(flow.indexOf('BuildSubmitValidatorSourceScript(job.ListingNumber)')<
    flow.indexOf('await submissionObserver.WaitForFormLoadAsync(token)'));
  console.log('PASS read-only site validator source inspection protects form, IDs and private values');
})().catch(e=>{console.error(e);process.exitCode=1});
