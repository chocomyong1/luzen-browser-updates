'use strict';
// Real Chromium input mechanics, with a LOCAL intercepted fake site page.
// No ItemMania credentials, network POST, or actual listing is involved.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');

const source = fs.readFileSync(path.join(__dirname, '../AppSource/SitePriceEdit.cs'), 'utf8');
const script = name => {
  const match = source.match(new RegExp(`public const string ${name} = """([\\s\\S]*?)""";`));
  assert(match, `missing ${name}`);
  return match[1];
};
const listingId = '2026112100145715';
const title = '10 레벨 겁화 삽니다 24 시';
const siteTitle = '1 ~ 9,995 레벨 겁화 삽니다 24 시';
const target = '35100';
const url = 'https://www.itemmania.com/myroom/buy/buy_re_reg.html';

async function oneTrial(browser, mutateTitle, maskPerKey=false, hoverMovesSubmit=false,
  dom0Cancels=false, multiLevel=false) {
  const recordTitle=multiLevel?'10 10 10 레벨 작열 구매합니다':title;
  const editTitle=multiLevel?'1 ~ 999 10 10 레벨 작열 구매합니다':siteTitle;
  const context = await browser.newContext({viewport:{width:1100,height:750}});
  const page = await context.newPage();
  const requests = [];
  page.on('request', req => {if(req.method()==='POST')requests.push(req.url());});
  await page.route(url, route => route.fulfill({status:200,contentType:'text/html',body:`
    <!doctype html><html lang="ko"><head><meta charset="utf-8"></head><body>
    <form id="frmBuy" action="/myroom/buy/buy_re_reg_ok.php" method="post">
      <input type="hidden" name="registration_id" value="${listingId}">
      <label>제목<input name="user_title" value="${editTitle}"></label>
      <table><tr><th>구매금액</th><td><input name="user_division_price" value="10,000"></td></tr></table>
      <button type="submit" id="ok_btn" class="buy_re_reg">재등록</button>
    </form>
    <script>
      window.keyboardCount=0;window.inputCount=0;window.changeCount=0;
      window.siteAttempted=false;window.siteAccepted=false;
      window.dom0HandlerRan=false;window.submitHasOwnPreventDefault=null;
      const price=document.querySelector('[name=user_division_price]');
      const form=document.querySelector('#frmBuy');
      if(${dom0Cancels?'true':'false'})form.onsubmit=function(){
        window.dom0HandlerRan=true;return false;
      };
      window.originalDom0SubmitHandler=form.onsubmit;
      form.addEventListener('submit',event=>{
        window.submitHasOwnPreventDefault=Object.prototype.hasOwnProperty.call(event,'preventDefault');
      },{capture:true});
      if(${hoverMovesSubmit?'true':'false'})document.querySelector('#ok_btn').addEventListener(
        'mouseenter',event=>{event.currentTarget.style.marginTop='30px';},{once:true});
      price.addEventListener('keyup',()=>window.keyboardCount++);
      if(${maskPerKey?'true':'false'})price.addEventListener('keyup',()=>{
        if(!price.dataset.masked){price.dataset.masked='1';price.value='10,000'+price.value;}
      });
      price.addEventListener('input',()=>window.inputCount++);
      price.addEventListener('change',()=>window.changeCount++);
      form.addEventListener('submit',event=>{
        window.siteAttempted=true;
        window.siteAccepted=window.keyboardCount>=5&&window.changeCount>=1&&
          price.value.replaceAll(',','')==='${target}';
        if(${mutateTitle?'true':'false'})form.querySelector('[name=user_title]').value='9 9 레벨 겁화 삽니다';
        event.preventDefault(); // fake site handles valid data itself
      });
    </script>
    </body></html>`}));
  await page.goto(url,{waitUntil:'domcontentloaded'});
  assert.equal((await page.locator('tr th').textContent()).trim(),'구매금액');
  assert.equal(await page.locator('[name=user_division_price]').inputValue(),'10,000');
  const prepared=await page.evaluate(script('EditFormScript').replace('__ARG__',JSON.stringify({
    number:listingId,oldPrice:'10000',newPrice:target,submit:true,
    expectedTitle:recordTitle,nativeSingleApproval:true,verifiedMinQuantity:'1',
    verifiedMaxQuantity:multiLevel?'999':'9995',allowVerifiedTitleRestore:true,
    trustedOneClick:true,nativeKeyboardPrice:true
  })));
  assert.equal(prepared.status,'input-ready',JSON.stringify(prepared));
  assert.equal(await page.locator('[name=user_division_price]').inputValue(),'10,000');
  const cdp=await context.newCDPSession(page);
  const move=(type,button,buttons,clickCount,x,y)=>cdp.send('Input.dispatchMouseEvent',
    {type,button,buttons,clickCount,x,y});
  const key=(type,key,code,windowsVirtualKeyCode,modifiers=0,text='')=>
    cdp.send('Input.dispatchKeyEvent',{type,key,code,windowsVirtualKeyCode,modifiers,
      text,unmodifiedText:text});
  await move('mouseMoved','none',0,0,prepared.x,prepared.y);
  assert.equal(await page.evaluate(script('VerifyNativePriceInputScript')),true);
  await move('mousePressed','left',1,1,prepared.x,prepared.y);
  await move('mouseReleased','left',0,1,prepared.x,prepared.y);
  assert.equal(await page.evaluate(script('VerifyNativePriceFocusScript')),true);
  await key('keyDown','a','KeyA',65,2);
  await key('keyUp','a','KeyA',65,2);
  for(const digit of target){
    await key('keyDown',digit,'Digit'+digit,48+Number(digit),0,digit);
    await key('keyUp',digit,'Digit'+digit,48+Number(digit));
  }
  await key('keyDown','Tab','Tab',9);
  await key('keyUp','Tab','Tab',9);
  const typed=await page.evaluate(()=>({
    price:document.querySelector('[name=user_division_price]').value,
    keyups:window.keyboardCount,inputs:window.inputCount,changes:window.changeCount
  }));
  if(!maskPerKey)
    assert.equal(typed.price,target,'Chromium key-down text must replace selected input without duplication');
  else assert.notEqual(typed.price,target,'Fake masked site must reproduce the real-world mismatch');
  assert(typed.keyups>=5,'Real keyup handlers must fire for each digit');
  assert(typed.changes>=1,'Real change event must fire when the input blurs');
  let finalized=await page.evaluate(script('FinalizeNativePriceScript'));
  if(maskPerKey){
    assert.equal(finalized.status,'input-mismatch');
    const selection=await page.evaluate(script('SelectNativePriceForReplacementScript'));
    assert.equal(selection.status,'retry-ready');
    await cdp.send('Input.insertText',{text:target});
    await key('keyDown','Tab','Tab',9);
    await key('keyUp','Tab','Tab',9);
    finalized=await page.evaluate(script('FinalizeNativePriceScript'));
  }
  assert.equal(finalized.status,'armed',finalized.message);
  assert.equal(await page.evaluate(()=>document.querySelector('#frmBuy').onsubmit===
    window.originalDom0SubmitHandler),true,'Site DOM0 submit handler must be preserved');
  await move('mouseMoved','none',0,0,finalized.x,finalized.y);
  let refreshed=await page.evaluate(script('RefreshNativeClickTargetScript'));
  assert.equal(refreshed.status,'click-ready',JSON.stringify(refreshed));
  if(Math.abs(refreshed.x-finalized.x)>1||Math.abs(refreshed.y-finalized.y)>1){
    await move('mouseMoved','none',0,0,refreshed.x,refreshed.y);
    const second=await page.evaluate(script('RefreshNativeClickTargetScript'));
    assert.equal(second.status,'click-ready',JSON.stringify(second));
    assert(Math.abs(second.x-refreshed.x)<=1&&Math.abs(second.y-refreshed.y)<=1,
      'The submit button must settle before one native click');
    refreshed=second;
  }
  assert.equal(await page.evaluate(script('VerifyNativeClickScript')),true);
  await move('mousePressed','left',1,1,refreshed.x,refreshed.y);
  await move('mouseReleased','left',0,1,refreshed.x,refreshed.y);
  const site=await page.evaluate(()=>({attempted:window.siteAttempted,accepted:window.siteAccepted,
    dom0HandlerRan:window.dom0HandlerRan,
    submitHasOwnPreventDefault:window.submitHasOwnPreventDefault,
    title:document.querySelector('[name=user_title]').value}));
  const probe=await page.evaluate(script('SubmitProbeScript'));
  assert(site.attempted);
  assert(site.accepted,'Fake-site keyboard/change validator must accept native price entry');
  assert.equal(requests.length,0,'Fake site must never POST');
  assert.equal(site.submitHasOwnPreventDefault,false,
    'Native submit event must retain its own unmodified preventDefault method');
  assert.match(probe,/키업\/입력\/변경\/포커스이탈:\d+\/\d+\/\d+\/\d+/,
    'Every real browser input category must be visible in the submit diagnostic');
  assert.match(probe,/제출 캡처:감지/);
  if(dom0Cancels){
    assert(site.dom0HandlerRan,'The native form must call the DOM0 onsubmit handler');
    assert.match(probe,/DOM0 onsubmit:연결/);
    assert.match(probe,/사이트 제출 취소:예/);
  }
  assert.match(probe,/취소 호출:/,'A prevented submit must identify the cancellation trace');
  assert.doesNotMatch(probe,/https?:\/\//,'Diagnostics must never expose a full site URL');
  if(mutateTitle){
    assert.match(probe,/제목 보호 차단:예/);
  }else{
    assert.equal(site.title,recordTitle);
    assert.match(probe,/제목 보호 차단:아니오/);
  }
  await context.close();
}

(async()=>{
  const options={headless:true,args:['--no-sandbox','--disable-dev-shm-usage']};
  if(process.env.LUZEN_TEST_CHROMIUM_BIN)options.executablePath=process.env.LUZEN_TEST_CHROMIUM_BIN;
  const browser=await chromium.launch(options);
  try{await oneTrial(browser,false);await oneTrial(browser,true);
    await oneTrial(browser,false,true);await oneTrial(browser,false,false,true);
    await oneTrial(browser,false,false,false,true);
    await oneTrial(browser,false,false,false,false,true);}
  finally{await browser.close();}
  console.log('PASS real Chromium key input triggers site validators; only one native click; late title mutation blocked');
})().catch(error=>{console.error(error);process.exitCode=1;});
