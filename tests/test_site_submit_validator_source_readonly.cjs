'use strict';
// A static-script GET-only diagnostic for the real site's canceled onsubmit.
// No session, account, price or live ItemMania request is used in this test.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const src=fs.readFileSync(path.join(__dirname,'../AppSource/SitePriceEdit.cs'),'utf8');
const flow=fs.readFileSync(path.join(__dirname,'../AppSource/AutomationForm.SitePriceEdit.cs'),'utf8');
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
      {src:`https://${host}/js/myroom/buy_re_reg.js?PRIVATE_TOKEN=abcdef`},
      {src:`https://${host}/js/editor.js?PRIVATE_VALUE=xyz`},
      {src:'https://cdn.itemmania.com/scripts/buy_re_reg.js?SENSITIVE=yes'},
      {src:'https://evil.invalid/js/buy_re_reg.js'}]:[]};
  const location={protocol:'https:',hostname:host,pathname:'/myroom/buy/buy_re_reg.html',
    href:`https://${host}/myroom/buy/buy_re_reg.html?PRIVATE_QUERY=foo`,origin:`https://${host}`};
  const fetch=async (url,options)=>{
    requested.push([url,options]);
    assert.equal(options.method,'GET');
    assert.equal(options.redirect,'error');
    assert(!url.includes('?') && url.startsWith(`https://${host}/js/`));
    return {ok:true,status:200,headers:{get:s=>s==='content-type'?'application/javascript':null},
      text:async()=>"const private_value='TOP_SECRET_RUNTIME_VALUE';\n"+
        "document.querySelector('#frmBuy').onsubmit=function(){if(!validPrice())return false;}"};
  };
  const run=vm.runInNewContext(script.replace('__ARG__',JSON.stringify({number:listing})),
    {location,document,URL,fetch},{timeout:1000});
  const result=await run;
  return {result,requested};
}
(async()=>{
  const {result,requested}=await inspect();
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
  assert.match(flow,/Clipboard\.SetText\(inspection\.Message \+ "\\n\\n" \+ sourceNote\)/);
  assert(flow.indexOf('BuildSubmitValidatorSourceScript(job.ListingNumber)')<
    flow.indexOf('using var submissionObserver = new SitePriceSubmitObserver'));
  console.log('PASS read-only site validator source inspection protects form, IDs and private values');
})().catch(e=>{console.error(e);process.exitCode=1});
