'use strict';
// The real site's FormChecker intentionally cancels the first submit event.
// This is an offline contract regression: no live item is changed.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'..');
const read=name=>fs.readFileSync(path.join(root,'AppSource',name),'utf8');
const site=read('SitePriceEdit.cs');
const flow=read('AutomationForm.SitePriceEdit.cs');
const observer=read('SitePriceSubmitObserver.cs');
const script=site.match(/public const string OfficialPriceSubmitReadinessScript = """([\s\S]*?)""";/)?.[1];
assert(script,'Missing native site asynchronous submit dependency check');
new vm.Script(script);

function check({host='www.itemmania.com',formReady=true,editorReady=true,
  ajaxReady=true,sanitizerReady=true,categoryReady=true}={}){
  const form=formReady?{onsubmit(){return false;}}:null;
  const document={querySelector:q=>q==='form#frmBuy'?form:null};
  const window={
    ajaxRequest:ajaxReady?()=>{}:null,
    createLayerContent:categoryReady?()=>{}:null,
    createLayerContentProceed:categoryReady?()=>{}:null,
    editor:editorReady?{sendImages:()=>Promise.resolve()}:null,
    DOMPurify:sanitizerReady?{sanitize:s=>s}:null
  };
  const location={protocol:'https:',hostname:host,
    pathname:'/myroom/buy/buy_re_reg.html'};
  return vm.runInNewContext(script,{location,document,window},{timeout:1000});
}

assert.equal(check().status,'ready');
for(const value of [
  {formReady:false}, {editorReady:false}, {ajaxReady:false},
  {sanitizerReady:false}, {categoryReady:false}, {host:'fake-itemmania.com'}
]) assert.equal(check(value).status,'error',JSON.stringify(value));

// The entire original first-party chain must be allowed to finish, while
// preserving the existing one-click and independently verified final price.
assert.match(flow,/SitePriceEdit\.OfficialPriceSubmitReadinessScript/);
assert(flow.indexOf('SitePriceEdit.OfficialPriceSubmitReadinessScript')<
  flow.indexOf('using var submissionObserver = new SitePriceSubmitObserver'));
assert.match(flow,/DispatchVerifiedSitePriceClickAsync\(final\.X, final\.Y, token, \(\) =>/);
assert.match(flow,/ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
assert.match(site,/AutomaticPriceSubmissionEnabled => false/);
assert.match(site,/초기 취소 해석:FormChecker 비동기 검증에서는 정상 가능/);

for(const endpoint of [
  '/_include/_user_contact_restrict.php',
  '/_include/_ajax_trade_auto_class_check.php',
  '/_include/_ajax_image_upload.php'
]) assert(observer.includes(endpoint),`Missing passive step ${endpoint}`);
assert.match(observer,/ValidationStage\(uri\.AbsolutePath\)/);
assert.match(observer,/StageSummary\(\)/);
assert.match(observer,/사이트 최종 재등록 POST 요청 미감지/);
assert.match(observer,/Network\.getResponseBody/);
assert.match(observer,/"S" => new ContactDecision\(true/);
assert.doesNotMatch(observer,/GetRequestPostData|postData|request\.headers/i);

const dialog=flow.slice(flow.indexOf('private void HandleSitePriceEditScriptDialog('));
assert.match(dialog,/CoreWebView2ScriptDialogKind\.Alert\)[\s\S]*?e\.Accept\(\)/);
assert.match(dialog,/if \(unsafeMessage \|\| e\.Kind !=/);
assert.match(dialog,/MessageBoxButtons\.YesNo, MessageBoxIcon\.Warning/);
assert.match(dialog,/if \(priceRepost\)[\s\S]*?e\.Accept\(\)/);
console.log('PASS first-party async submit readiness, safe alerts, stage tracing and exact-ID readback');
