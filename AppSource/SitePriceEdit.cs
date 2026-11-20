using System;
using System.Text.Json;

namespace LuzenBrowserReplica;

// Runs only after the user explicitly requests a price change. The signed-in
// OWN-list row is matched by an ID-bound control/attribute, not its decorative
// title; the edit form must independently prove the ID and old price.
internal sealed class SitePriceEditResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string Price { get; set; } = "";
    public int Candidates { get; set; }
}

internal static class SitePriceEdit
{
    // Deliberately hard-locked until a safe price-only endpoint or native
    // submit contract is proven on the actual service. See EditFormScript.
    public static bool AutomaticPriceSubmissionEnabled => false;
    public static SitePriceEditResult Decode(string? raw)
    {
        try
        {
            return JsonSerializer.Deserialize<SitePriceEditResult>(raw ?? "", new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true }) ?? new SitePriceEditResult { Message = "사이트 응답 없음" };
        }
        catch (JsonException) { return new SitePriceEditResult { Message = "수정 화면 응답을 해석하지 못했습니다" }; }
    }

    public static string BuildOwnedRowScript(string number, string title, bool click,
        string minQuantity = "", string maxQuantity = "", string server = "", string tradeSide = "") =>
        OwnedListingTitleIdentity.Inject(OwnedRowScript).Replace("__ARG__", JsonSerializer.Serialize(new
        { number, title, click, openTitle = false, minQuantity, maxQuantity, server, tradeSide }), StringComparison.Ordinal);

    public static string BuildOwnedTitleScript(string number, string title, bool click,
        string minQuantity = "", string maxQuantity = "", string server = "", string tradeSide = "") =>
        OwnedListingTitleIdentity.Inject(OwnedRowScript).Replace("__ARG__", JsonSerializer.Serialize(new
        { number, title, click, openTitle = true, minQuantity, maxQuantity, server, tradeSide }), StringComparison.Ordinal);

    public static string BuildDetailEditScript(string number, string title, bool click) =>
        DetailEditScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, title, click }), StringComparison.Ordinal);

    public static string BuildEditFormScript(string number, string oldPrice, string newPrice, bool submit,
        string expectedTitle = "") =>
        EditFormScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, oldPrice, newPrice, submit, expectedTitle }), StringComparison.Ordinal);

    public const string OwnedRowScript = """
    (() => {
      const a=__ARG__;
      const norm=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
      __OWNED_TITLE_IDENTITY__
      const result=(status,message='',candidates=0)=>({status,message,candidates});
      const official=location.protocol==='https:'&&/(^|\.)itemmania\.com$/i.test(location.hostname);
      const own=/^\/myroom\/(sell\/sell_regist|buy\/buy_regist)\.html$/i.test(location.pathname);
      if(!official||!own)return result('error','본인 판매·구매 관리목록 화면이 아닙니다');
      if(!/^\d{8,20}$/.test(a.number))return result('error','등록번호가 유효하지 않습니다');
      const exact=new RegExp('(?:^|\\D)'+a.number+'(?!\\d)');
      const candidates=new Set();
      const docs=[document],seen=new Set(docs);
      for(let i=0;i<docs.length&&i<8;i++){
        for(const frame of docs[i].querySelectorAll('iframe,frame')){
          try{const d=frame.contentDocument;if(d&&!seen.has(d)){seen.add(d);docs.push(d)}}catch{}
        }
      }
      for(const d of docs){
        for(const row of d.querySelectorAll('tr,[data-tid],[data-listing-id],[data-goods-id],[data-trade-id]')){
          const container=row.closest('tr,li,article')||row;
          if(String(container.innerText||'').length<5000)candidates.add(container);
        }
        for(const e of d.querySelectorAll('[onclick]')){
          if(exact.test(String(e.getAttribute('onclick')||''))){
            const row=e.closest('tr,li,article');if(row)candidates.add(row);
          }
        }
      }
      const matching=[];
      let identityProblem='';
      for(const row of candidates){
        const text=String(row.innerText||row.textContent||'');
        const ids=new Set();
        let strongNumber=false;
        for(const e of [row,...row.querySelectorAll('*')].slice(0,300)){
          for(const attr of ['data-tid','data-listing-id','data-goods-id','data-trade-id','data-id']){
            const id=e.getAttribute?.(attr)||'';
            if(/^\d{8,20}$/.test(id)){
              ids.add(id);
              if(id===a.number)strongNumber=true;
            }
          }
          for(const attr of ['onclick','href','data-url','data-href']){
            const val=e.getAttribute?.(attr)||'';
            if(exact.test(val))ids.add(a.number);
            // In current My Room markup the exact native repost/edit handler
            // binds the listing even if there is no data-tid on the row.
            if(attr==='onclick' && /^\s*(?:reInsert|modifyItem|editItem)\s*\(\s*(['"])(\d{8,20})\1\s*[,)]/.exec(val)?.[2]===a.number)
              strongNumber=true;
          }
        }
        if(exact.test(text))ids.add(a.number);
        if(!ids.has(a.number))continue;
        const identity=ownedTitleIdentity(row,a);
        if(!identity.ok||!strongNumber){
          identityProblem=!strongNumber?'등록번호가 행 데이터·수정 함수에 직접 연결되지 않았습니다':identity.reason;
          continue;
        }
        if(ids.size!==1)return result('error','해당 행에 서로 다른 등록번호가 있어 수정하지 않았습니다');
        matching.push(row);
      }
      if(matching.length!==1)return result('error',matching.length?
        '동일 등록번호의 검증된 행이 여러 개입니다':
        (identityProblem||'등록번호가 연결된 본인 물품 행이 없습니다'),matching.length);
      const row=matching[0];
      const visible=e=>e.isConnected&&e.getClientRects().length>0&&!e.disabled&&
        (e.ownerDocument?.defaultView?.getComputedStyle(e)||getComputedStyle(e)).visibility!=='hidden';
      if(a.openTitle){
        const titleLinks=[...row.querySelectorAll('.trade_title')].filter(visible);
        if(titleLinks.length!==1)return result('error',titleLinks.length?
          '등록글 제목이 여러 개 연결되어 있습니다':'검증된 등록글의 클릭 가능한 제목을 찾지 못했습니다',titleLinks.length);
        const titleNode=titleLinks[0];
        let target=titleNode;
        for(let el=titleNode;el&&el!==row;el=el.parentElement){
          if(el.matches?.('a[href],a[onclick],button,[role="button"],[onclick]')){
            target=el;break;
          }
        }
        const clickable=target!==titleNode||titleNode.matches?.('a[href],a[onclick],button,[onclick]')||
          getComputedStyle(titleNode).cursor==='pointer';
        if(!clickable)return result('error','제목은 보이지만 클릭할 수 있는 링크·동작을 확인하지 못했습니다');
        if(!a.click)return result('ready','등록번호가 일치하는 상세 화면 링크 확인',1);
        target.click();
        return result('opened','검증된 등록글 제목 클릭',1);
      }
      const label=e=>norm([e.innerText,e.value,e.getAttribute('title'),e.getAttribute('aria-label'),
        e.getAttribute('data-desc'),e.getAttribute('data-tooltip'),e.getAttribute('data-original-title'),
        ...[...(e.querySelectorAll?.('[title],[aria-label],svg title')||[])].slice(0,8).map(x=>
          [x.getAttribute('title'),x.getAttribute('aria-label'),x.textContent].filter(Boolean).join(' '))]
        .filter(Boolean).join(' '));
      const edit=[];
      const debug=[];
      for(const e of row.querySelectorAll('a,button,input[type="button"],input[type="submit"],[role="button"],[onclick]')){
        if(!visible(e))continue;
        const words=label(e),handler=String(e.getAttribute('onclick')||'');
        const classname=String(e.className||'').toLowerCase();
        const isEdit=/(수정|편집|edit|modify)/i.test(words+' '+classname+' '+handler);
        const forbidden=/(삭제|취소|거래신청|결제|유료|정지|숨김|재등록|reregist|reinsert)/i.test(words+' '+classname+' '+handler);
        if(debug.length<12)debug.push([String(e.tagName||'control').toLowerCase(),String(e.getAttribute('data-desc')||'').slice(0,30),
          classname.slice(0,45),handler.replace(/\d{8,20}/g,'[등록번호]').slice(0,65)].join('/'));
        if(isEdit&&!forbidden)edit.push(e);
      }
      if(edit.length!==1)return result('error',(edit.length?
        '해당 글의 수정 버튼이 여러 개라 중단했습니다':'해당 글의 수정 버튼을 찾지 못했습니다')+
        ' [진단: 문서 '+docs.length+'개, 후보 '+debug.join(' | ').slice(0,650)+']',edit.length);
      if(!a.click)return result('ready','정확한 등록번호의 수정 버튼을 확인했습니다',1);
      edit[0].click();
      return result('opened','검증된 등록글의 수정 화면 열기 요청',1);
    })()
    """;

    // The current My Room cards show only reInsert/hide/delete icons. Clicking
    // the title opens the registered listing detail where '수정하기' is present.
    // This script never clicks until both listing identity and the unique edit
    // control are independently verified on that read-only detail page.
    public const string DetailEditScript = """
    (() => {
      const a=__ARG__;
      const result=(status,message='',candidates=0)=>({status,message,candidates});
      const norm=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname))
        return result('error','아이템매니아 공식 HTTPS 화면이 아닙니다');
      const isList=/^\/myroom\/(sell\/sell_regist|buy\/buy_regist)\.html$/i.test(location.pathname);
      const isKnownDetail=/^\/myroom\/(sell\/sell_regist_view|buy\/buy_regist_view)\.html$/i.test(location.pathname);
      if(isList)return result('error','물품 상세 화면이 아닌 관리 목록입니다');
      if(!/^\d{8,20}$/.test(a.number))
        return result('error','등록번호가 유효하지 않습니다');
      const exact=new RegExp('(?:^|\\D)'+a.number+'(?!\\d)');
      const page=String(document.body?.innerText||'');
      const path=location.pathname+location.search;
      const displayedIds=[...new Set([...page.matchAll(/#\s*(\d{8,20})(?!\d)/g)].map(m=>m[1]))];
      if(displayedIds.length>1||(displayedIds.length===1&&displayedIds[0]!==a.number))
        return result('error','상세 화면에 다른 등록번호가 표시되어 수정하지 않았습니다');
      const independentlyIdentified=exact.test(path)||
        (isKnownDetail&&displayedIds.length===1&&displayedIds[0]===a.number);
      if(!independentlyIdentified||!exact.test(page))
        return result('error','상세 화면의 등록번호를 독립적으로 확인하지 못했습니다');
      const visible=e=>e.isConnected&&e.getClientRects().length>0&&!e.disabled&&
        getComputedStyle(e).visibility!=='hidden';
      const buttons=[...document.querySelectorAll('a,button,input[type="button"],input[type="submit"],[role="button"]')]
        .filter(visible).filter(e=>norm([e.innerText,e.value,e.getAttribute('aria-label'),
          e.getAttribute('title')].filter(Boolean).join(' '))==='수정하기');
      if(buttons.length!==1)return result('error',buttons.length?
        '상세 화면의 수정하기 버튼이 여러 개입니다':'상세 화면의 수정하기 버튼을 찾지 못했습니다',buttons.length);
      if(!a.click)return result('ready','등록번호·수정하기 버튼을 확인했습니다',1);
      buttons[0].click();
      return result('opened','검증된 상세 화면의 수정하기 버튼을 눌렀습니다',1);
    })()
    """;

    // The screenshot supplied by the user shows a row labelled '구매금액'
    // containing both '1개당' and the actual unit-price input. Match the
    // current SITE price, never an arbitrary first numeric input.
    // IMPORTANT: The site's character-name editor also has a '수정하기'
    // button. Never infer submission from a button label. The single audited
    // path is the owned BUY re-registration form (frmBuy) with a unique
    // button#ok_btn.buy_re_reg[type=submit]. Other pages stay read-only.
    public const string EditFormScript = """
    (() => {
      const a=__ARG__;
      const result=(status,message='',price='',candidates=0)=>({status,message,price,candidates});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname))
        return result('error','아이템매니아 공식 HTTPS 페이지가 아닙니다');
      if(!/^\d{8,20}$/.test(a.number))return result('error','등록번호가 유효하지 않습니다');
      const money=s=>String(s||'').replace(/[,원\s₩]/g,'');
      const desired=money(a.newPrice),old=money(a.oldPrice);
      if(!/^\d+$/.test(desired)||Number(desired)<1000||Number(desired)%10!==0)
        return result('error','변경 가격은 1,000원 이상, 10원 단위여야 합니다');
      if(!/^\d+$/.test(old)||Number(old)<1000)return result('error','현재 사이트 가격을 확인하지 못했습니다');
      const re=new RegExp('(?:^|\\D)'+a.number+'(?!\\d)');
      const urlMatches=re.test(location.pathname+location.search);
      const forms=[...document.querySelectorAll('form')];
      const idFields=[...document.querySelectorAll('input[type="hidden"],[data-tid],[data-listing-id],[data-goods-id]')];
      const idMatches=idFields.some(e=>[e.value,e.getAttribute('data-tid'),e.getAttribute('data-listing-id'),
        e.getAttribute('data-goods-id')].some(v=>String(v||'')===a.number));
      if(!urlMatches&&!idMatches)return result('error','현재 수정 화면의 등록번호를 독립적으로 확인할 수 없습니다');
      const visible=e=>e.isConnected&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
      const editable=e=>e.tagName==='INPUT'&&visible(e)&&!e.disabled&&!e.readOnly&&
        !['hidden','password','radio','checkbox','submit','button'].includes((e.type||'').toLowerCase());
      const matching=[];
      // Strongly identified Korean amount rows; avoid the adjacent '1개당' control.
      for(const row of document.querySelectorAll('tr,[role="row"]')){
        const heading=String(row.querySelector('th,[role="rowheader"],td:first-child')?.textContent||'').replace(/\s/g,'');
        if(!/(구매금액|판매금액|구매가격|판매가격)/.test(heading))continue;
        for(const input of row.querySelectorAll('input'))if(editable(input)&&money(input.value)===old)
          matching.push(input);
      }
      if(matching.length===0){
        const alias=/^(price|goods_price|sale_price|buy_price|sell_price|trade_price|goodsPrice|buyPrice|sellPrice)$/i;
        for(const input of document.querySelectorAll('input'))if(editable(input)&&
          alias.test(input.name||input.id||'')&&money(input.value)===old)matching.push(input);
      }
      if(matching.length!==1)return result('error',matching.length?
        '가격 입력칸이 여러 개라 수정하지 않았습니다':'현재 사이트 가격과 일치하는 가격 입력칸을 찾지 못했습니다','',matching.length);
      const priceInput=matching[0];
      const form=priceInput.closest('form')||(forms.length===1?forms[0]:null);
      const safe=s=>String(s||'').replace(/[\r\n|]/g,' ').replace(/\s+/g,' ').trim().slice(0,43);
      // Read attributes, not e.id: HTMLFormElement.id can be shadowed by
      // an input named "id", which previously printed [object HTMLInputElement].
      const attr=(e,k)=>safe(e?.getAttribute?.(k));
      const selector='button,input[type="submit"],input[type="button"],input[type="image"],'+
        'a[onclick],a[href^="javascript:"],[role="button"],[data-action]';
      const controls=[...document.querySelectorAll(selector)].filter(visible);
      const details=controls.map((e,i)=>{
        const label=safe(e.innerText||e.value||attr(e,'aria-label')||attr(e,'title'))||'-';
        const handlerSource=String(e.getAttribute?.('onclick')||e.getAttribute?.('href')||'');
        const handler=handlerSource.match(/([\w$]+)\s*\(/)?.[1]||safe(e.onclick?.name)||'-';
        const row=e.closest?.('tr,[role="row"]');
        const section=safe(row?.querySelector?.('th,[role="rowheader"],td:first-child')?.textContent)||'-';
        const relation=form?(e.closest?.('form')===form?'같은폼':'다른영역'):'폼미확인';
        const ident=attr(e,'id')||attr(e,'name')||'-';
        const className=attr(e,'class')||'-';
        return {index:i+1,label,section,relation,ident,className,handler,
          line:`#${i+1} ${label} [${section}; ${relation}; ${safe(e.tagName)}/${safe(e.type)}; `+
            `id:${ident}; class:${className}; fn:${safe(handler)}]`};
      });
      const keywords=/수정|변경|등록|저장|완료|확인|적용|제출|submit|modify|edit|regist|save|update/i;
      const candidates=details.filter(d=>keywords.test([d.label,d.ident,d.className,d.handler].join(' ')));
      // Do not report the first 18 menu buttons again. Show actionable
      // candidates and the LAST controls, where the price form footer lives.
      const focus=candidates.slice(-18);
      const tail=details.slice(-10).filter(d=>!focus.includes(d));
      const formName=attr(form,'id')||attr(form,'name')||'-';
      const formAction=safe(String(form?.getAttribute?.('action')||'').split('?')[0])||'-';
      const priceName=attr(priceInput,'name')||attr(priceInput,'id')||safe(priceInput.name)||'-';
      const isAuditedBuyForm=location.pathname.toLowerCase()==='/myroom/buy/buy_re_reg.html'&&
        form?.tagName==='FORM'&&formName==='frmBuy'&&priceName==='user_division_price';
      const formOwnedId=isAuditedBuyForm&&[...form.querySelectorAll('input[type="hidden"]')]
        .some(e=>String(e.value||'')===a.number);
      // A price change sends the site's ENTIRE re-registration form, not just
      // the amount. Never send it unless its original title survives intact.
      // Do not "repair" a corrupted title by guessing from quantity/level.
      const cleanTitle=s=>String(s||'').replace(/\s+/g,' ').trim();
      const titleName=/^(?:title|subject|goods_?title|item_?title|user_?title|goods_?subject|item_?subject|user_?subject|goods_?name|item_?name|user_?item_?name)$/i;
      const titleInputs=form?[...form.querySelectorAll('input,textarea')].filter(e=>
        titleName.test(String(e.getAttribute?.('name')||e.name||e.getAttribute?.('id')||''))):[];
      const originalTitle=cleanTitle(a.expectedTitle);
      const siteTitle=titleInputs.length===1?cleanTitle(titleInputs[0].value):'';
      const titlePreserved=originalTitle.length>=3&&titleInputs.length===1&&
        siteTitle===originalTitle;
      const siteSubmit=isAuditedBuyForm?[...form.querySelectorAll('button[type="submit"]')].filter(e=>
        visible(e)&&attr(e,'id')==='ok_btn'&&attr(e,'class').split(/\s+/).includes('buy_re_reg')&&
        String(e.innerText||e.textContent||'').replace(/\s+/g,'')==='재등록'):[];
      // All of path, form, old site price, hidden listing ID, and the EXACT
      // native submit control must agree. A character-name save cannot pass.
      if(formOwnedId&&siteSubmit.length===1){
        if(!titlePreserved)return result('error',titleInputs.length!==1?
          '가격 수정은 제목까지 재등록하므로 제목 입력칸을 유일하게 확인할 때까지 제출을 차단합니다':
          '사이트 수정폼 제목과 저장된 원본 제목이 다릅니다. 가격만 수정해도 제목이 바뀔 수 있어 제출을 차단합니다');
        // Safety interlock: the former code posted the ENTIRE site form with
        // fetch(FormData(form)), bypassing the site's submit handler. This can
        // rewrite the listing title even when only its price was edited. Until
        // the server-side title behavior is verified, NEVER send that POST.
        // Preview and submit requests are deliberately read-only.
        return result('blocked','제목 보호 모드: 구매글 가격 자동 수정은 중지됐습니다. 사이트에서 직접 재등록하세요.',old,1);
      }
      const diag=`경로:${safe(location.pathname)}; 가격칸:${priceName}; 폼:${formName}/${formAction}; `+
        `폼 등록번호:${formOwnedId?'일치':'미확인'}; 제출후보:${siteSubmit.length}개; `+
        `컨트롤:${controls.length}개, 작업 후보:${candidates.length}개\n`+
        `작업 후보(뒤쪽 우선):\n${focus.map(d=>d.line).join('\n')||'없음'}\n`+
        `화면 끝부분:\n${tail.map(d=>d.line).join('\n')||'후보에 포함됨'}`;
      // This is a hard safety interlock for every non-audited page/control,
      // even when submit=true. Never fall back to another labeled button.
      return result('diagnostic','제출 차단(폼 검증 불충분, 읽기 전용).\n'+diag.slice(0,4300),old,controls.length);
    })()
    """;
}
