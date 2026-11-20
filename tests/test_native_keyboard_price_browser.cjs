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

async function oneTrial(browser, mutateTitle) {
  const context = await browser.newContext({viewport:{width:1100,height:750}});
  const page = await context.newPage();
  const requests = [];
  page.on('request', req => {if(req.method()==='POST')requests.push(req.url());});
  await page.route(url, route => route.fulfill({status:200,contentType:'text/html',body:`
    <!doctype html><html lang="ko"><body>
    <form id="frmBuy" action="/myroom/buy/buy_re_reg_ok.php" method="post">
      <input type="hidden" name="registration_id" value="${listingId}">
      <label>제목<input name="user_title" value="${siteTitle}"></label>
      <table><tr><th>구매금액</th><td><input name="user_division_price" value="10,000"></td></tr></table>
      <button type="submit" id="ok_btn" class="buy_re_reg">재등록</button>
    </form>
    <script>
      window.keyboardCount=0;window.inputCount=0;window.changeCount=0;
      window.siteAttempted=false;window.siteAccepted=false;
      const price=document.querySelector('[name=user_division_price]');
      const form=document.querySelector('#frmBuy');
      price.addEventListener('keyup',()=>window.keyboardCount++);
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
    expectedTitle:title,nativeSingleApproval:true,verifiedMinQuantity:'1',
    verifiedMaxQuantity:'9995',allowVerifiedTitleRestore:true,
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
  assert.equal(typed.price,target,'Chromium key-down text must replace selected input without duplication');
  assert(typed.keyups>=5,'Real keyup handlers must fire for each digit');
  assert(typed.changes>=1,'Real change event must fire when the input blurs');
  const finalized=await page.evaluate(script('FinalizeNativePriceScript'));
  assert.equal(finalized.status,'armed',finalized.message);
  await move('mouseMoved','none',0,0,finalized.x,finalized.y);
  assert.equal(await page.evaluate(script('VerifyNativeClickScript')),true);
  await move('mousePressed','left',1,1,finalized.x,finalized.y);
  await move('mouseReleased','left',0,1,finalized.x,finalized.y);
  const site=await page.evaluate(()=>({attempted:window.siteAttempted,accepted:window.siteAccepted,
    title:document.querySelector('[name=user_title]').value}));
  const probe=await page.evaluate(script('SubmitProbeScript'));
  assert(site.attempted);
  assert(site.accepted,'Fake-site keyboard/change validator must accept native price entry');
  assert.equal(requests.length,0,'Fake site must never POST');
  if(mutateTitle){
    assert.match(probe,/제목 보호 차단:예/);
  }else{
    assert.equal(site.title,title);
    assert.match(probe,/제목 보호 차단:아니오/);
  }
  await context.close();
}

(async()=>{
  const options={headless:true,args:['--no-sandbox','--disable-dev-shm-usage']};
  if(process.env.LUZEN_TEST_CHROMIUM_BIN)options.executablePath=process.env.LUZEN_TEST_CHROMIUM_BIN;
  const browser=await chromium.launch(options);
  try{await oneTrial(browser,false);await oneTrial(browser,true);}
  finally{await browser.close();}
  console.log('PASS real Chromium key input triggers site validators; only one native click; late title mutation blocked');
})().catch(error=>{console.error(error);process.exitCode=1;});
