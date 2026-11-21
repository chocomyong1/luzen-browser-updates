'use strict';
// Offline guard for one-shot price edits: no scheduler and no site requests.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const read=name=>fs.readFileSync(path.join(__dirname,'../AppSource',name),'utf8');
const app=read('AutomationForm.SitePriceEdit.cs');
const grid=read('AutomationForm.cs');
const main=read('MainForm.cs');
const site=read('SitePriceEdit.cs');

// The per-row action must use DataBoundItem, not bulk selection or a stale
// editor row. Header and editor actions share the same explicit one-shot path.
assert.match(grid,/Name = "ImmediatePriceEdit", HeaderText = "즉시 가격 수정"/);
assert.match(grid,/Name != "ImmediatePriceEdit"\) return;[\s\S]*?DataBoundItem is ListingJob target\)[\s\S]*?ApplySelectedSitePriceAsync\(target\)/);
assert.match(grid,/FooterButton\("1건 즉시 가격 수정"/);
assert.match(main,/선택 1건 즉시 가격 수정 \(실제 제출\)/);
assert.match(main,/ApplySelectedSitePriceFromHeaderAsync\(\)/);
const immediate=app.slice(app.indexOf('private async Task ApplySelectedSitePriceAsync('),
  app.indexOf('// Never edits a field or submits a listing.'));
assert.match(immediate,/rowTarget \?\?/);
assert.match(immediate,/!_jobs\.Contains\(job\) \|\| !IsRegisteredOwnedBuyPriceTarget\(job\)/);
assert.match(immediate,/if \(job\.RequiresReview\)/);
assert.match(immediate,/job\.PendingSitePriceTarget/);
assert.match(immediate,/if \(_schedulerRunning\) PauseScheduler\(\);/);
assert.match(immediate,/ApplySitePriceAsync\(job, bulkApproval: false, nativeSingleApproval: true/);
assert.doesNotMatch(immediate,/StartScheduler\(|StartSelectedSystems|ExecuteJobAsync\(/);
assert.match(app,/!nativeSingleApproval\) SaveSelectedJob\(false\)/);
assert.match(app,/ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
assert.match(site,/AutomaticPriceSubmissionEnabled => false/);

// The real site's FormChecker checks user_text BEFORE its AJAX chain, while
// its Quill editor syncs user_text on text-change. Ensure the one-off path
// mirrors only the existing editor content, never title or listing number.
const preflight=site.match(/public const string OfficialPricePreflightScript = """([\s\S]*?)""";/)?.[1];
assert(preflight,'Site preflight missing');
new vm.Script(preflight);
function inspect({text='',html='<p>Existing details</p>',minimum='1',maximum='100',unit='1'}={}){
  const fields={
    user_title:{value:'9레벨 겁화 구매합니다'},user_text:{value:text},
    user_goods:{value:'item'},game_code:{value:'1835'},
    user_goods_type:{value:'division'},
    user_quantity_min_tmp:{value:minimum},user_quantity_max_tmp:{value:maximum},
    user_quantity_min:{value:minimum},user_quantity_max:{value:maximum},
    user_division_unit:{value:unit},user_division_price:{value:'8,000'},
    user_character:{value:'캐릭터명'},user_contactA:{value:'Y'},slctMobile_type:{value:'Y'}
  };
  const form={getAttribute:name=>name==='id'?'frmBuy':'',
    checker:{},onsubmit(){return false;},
    elements:{namedItem:name=>fields[name]??null}};
  const context={location:{protocol:'https:',hostname:'www.itemmania.com',
    pathname:'/myroom/buy/buy_re_reg.html'},document:{forms:{frmBuy:form}},
    window:{editor:{root:{innerHTML:html}},DOMPurify:{sanitize:s=>s}},initialPrice:1000,
    _marketPriceBlocked:false};
  const result=vm.runInNewContext(preflight,context,{timeout:1000});
  return {result,fields};
}
const synced=inspect();
assert.equal(synced.result.status,'ready');
assert.equal(synced.fields.user_text.value,'<p>Existing details</p>');
assert.equal(synced.fields.user_title.value,'9레벨 겁화 구매합니다');
const already=inspect({text:'ORIGINAL'});
assert.equal(already.fields.user_text.value,'ORIGINAL');
assert.equal(inspect({html:'<p><br></p>'}).result.status,'error');
assert.equal(inspect({minimum:'0'}).result.status,'error');
assert.equal(inspect({unit:'0'}).result.status,'error');
assert.match(app,/SitePriceEdit\.OfficialPricePreflightScript/);
assert(app.indexOf('SitePriceEdit.OfficialPricePreflightScript')<
  app.indexOf('using var submissionObserver = new SitePriceSubmitObserver'));
console.log('PASS: immediate single listing row action, scheduler isolation and official-form preflight');
