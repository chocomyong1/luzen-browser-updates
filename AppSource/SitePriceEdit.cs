using System;
using System.Text.Json;

namespace LuzenBrowserReplica;

// Runs only after the user explicitly requests a price change. A list row is
// matched by its registration number AND title; the edit form must independently
// prove the registration number and old price before any real-site submission.
internal sealed class SitePriceEditResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string Price { get; set; } = "";
    public int Candidates { get; set; }
}

internal static class SitePriceEdit
{
    public static SitePriceEditResult Decode(string? raw)
    {
        try
        {
            return JsonSerializer.Deserialize<SitePriceEditResult>(raw ?? "", new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true }) ?? new SitePriceEditResult { Message = "사이트 응답 없음" };
        }
        catch (JsonException) { return new SitePriceEditResult { Message = "수정 화면 응답을 해석하지 못했습니다" }; }
    }

    public static string BuildOwnedRowScript(string number, string title, bool click) =>
        OwnedRowScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, title, click, openTitle = false }), StringComparison.Ordinal);

    public static string BuildOwnedTitleScript(string number, string title, bool click) =>
        OwnedRowScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, title, click, openTitle = true }), StringComparison.Ordinal);

    public static string BuildDetailEditScript(string number, string title, bool click) =>
        DetailEditScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, title, click }), StringComparison.Ordinal);

    public static string BuildEditFormScript(string number, string oldPrice, string newPrice, bool submit) =>
        EditFormScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, oldPrice, newPrice, submit }), StringComparison.Ordinal);

    public const string OwnedRowScript = """
    (() => {
      const a=__ARG__;
      const norm=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
      const result=(status,message='',candidates=0)=>({status,message,candidates});
      const official=location.protocol==='https:'&&/(^|\.)itemmania\.com$/i.test(location.hostname);
      const own=/^\/myroom\/(sell\/sell_regist|buy\/buy_regist)\.html$/i.test(location.pathname);
      if(!official||!own)return result('error','본인 판매·구매 관리목록 화면이 아닙니다');
      if(!/^\d{8,20}$/.test(a.number)||norm(a.title).length<3)return result('error','등록번호 또는 제목이 유효하지 않습니다');
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
      const core=s=>norm(s).replace(/^\[[\d,]+개[~〜-][\d,]+개\]/,'');
      const matching=[];
      for(const row of candidates){
        const text=String(row.innerText||row.textContent||'');
        const ids=new Set();
        for(const e of [row,...row.querySelectorAll('*')].slice(0,300)){
          for(const attr of ['data-tid','data-listing-id','data-goods-id','data-trade-id','data-id']){
            const id=e.getAttribute?.(attr)||'';if(/^\d{8,20}$/.test(id))ids.add(id);
          }
          for(const attr of ['onclick','href','data-url','data-href']){
            const val=e.getAttribute?.(attr)||'';
            if(exact.test(val))ids.add(a.number);
          }
        }
        if(exact.test(text))ids.add(a.number);
        if(!ids.has(a.number))continue;
        const title=String(row.querySelector('.trade_title')?.textContent||'');
        if(!norm(text).includes(norm(a.title))&&core(title)!==core(a.title))continue;
        if(ids.size!==1)return result('error','해당 행에 서로 다른 등록번호가 있어 수정하지 않았습니다');
        matching.push(row);
      }
      if(matching.length!==1)return result('error',matching.length?
        '등록번호·제목이 일치하는 행이 여러 개입니다':'등록번호·제목이 일치하는 행이 없습니다',matching.length);
      const row=matching[0];
      const visible=e=>e.isConnected&&e.getClientRects().length>0&&!e.disabled&&
        (e.ownerDocument?.defaultView?.getComputedStyle(e)||getComputedStyle(e)).visibility!=='hidden';
      if(a.openTitle){
        const titleLinks=[...row.querySelectorAll('.trade_title')].filter(visible)
          .filter(e=>core(e.textContent)===core(a.title));
        if(titleLinks.length!==1)return result('error',titleLinks.length?
          '등록글 제목이 여러 개 연결되어 있습니다':'검증된 등록글의 클릭 가능한 제목을 찾지 못했습니다',titleLinks.length);
        if(!a.click)return result('ready','등록번호·제목이 일치하는 상세 화면 제목 링크 확인',1);
        titleLinks[0].click();
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
      if(!a.click)return result('ready','정확한 등록번호·제목의 수정 버튼을 확인했습니다',1);
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
      const core=s=>norm(s).replace(/^\[[\d,]+개[~〜-][\d,]+개\]/,'');
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname))
        return result('error','아이템매니아 공식 HTTPS 화면이 아닙니다');
      if(/^\/myroom\//i.test(location.pathname))
        return result('error','물품 상세 화면이 아닌 관리 목록입니다');
      if(!/^\d{8,20}$/.test(a.number)||core(a.title).length<3)
        return result('error','등록번호 또는 제목이 유효하지 않습니다');
      const exact=new RegExp('(?:^|\\D)'+a.number+'(?!\\d)');
      const page=String(document.body?.innerText||'');
      const path=location.pathname+location.search;
      if(!exact.test(path)||!exact.test(page))
        return result('error','상세 화면 URL과 본문에서 동일한 등록번호를 확인하지 못했습니다');
      if(!core(page).includes(core(a.title)))
        return result('error','상세 화면 제목이 선택한 등록글과 다릅니다');
      const visible=e=>e.isConnected&&e.getClientRects().length>0&&!e.disabled&&
        getComputedStyle(e).visibility!=='hidden';
      const buttons=[...document.querySelectorAll('a,button,input[type="button"],input[type="submit"],[role="button"]')]
        .filter(visible).filter(e=>norm([e.innerText,e.value,e.getAttribute('aria-label'),
          e.getAttribute('title')].filter(Boolean).join(' '))==='수정하기');
      if(buttons.length!==1)return result('error',buttons.length?
        '상세 화면의 수정하기 버튼이 여러 개입니다':'상세 화면의 수정하기 버튼을 찾지 못했습니다',buttons.length);
      if(!a.click)return result('ready','등록번호·제목·수정하기 버튼을 확인했습니다',1);
      buttons[0].click();
      return result('opened','검증된 상세 화면의 수정하기 버튼을 눌렀습니다',1);
    })()
    """;

    // The screenshot supplied by the user shows a row labelled '구매금액'
    // containing both '1개당' and the actual unit-price input. Match the
    // current SITE price, never an arbitrary first numeric input.
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
      const root=form||document;
      const submitControls=scope=>[...scope.querySelectorAll('button,input[type="submit"],input[type="button"],a[onclick]')]
        .filter(visible).filter(e=>{
          const t=String(e.innerText||e.value||'').replace(/\s+/g,'').trim();
          return /^(재등록|수정|수정하기|변경|변경하기|저장|저장하기)$/.test(t);
        });
      let buttons=submitControls(root);
      // Some older templates place the footer outside the <form>. Only a
      // SINGLE site-wide button with the exact submit label is acceptable.
      if(buttons.length===0&&root!==document)buttons=submitControls(document);
      if(buttons.length!==1)return result('error',buttons.length?
        '수정 제출 버튼이 여러 개라 중단했습니다':'재등록/수정 제출 버튼을 확인하지 못했습니다',old,buttons.length);
      if(!a.submit)return result('ready','사이트 등록번호·현재 단가·수정 제출 버튼 확인',old,1);
      if(money(priceInput.value)!==old)return result('error','제출 직전 현재 사이트 가격이 변경됐습니다');
      const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')?.set;
      if(!setter)return result('error','사이트 가격 입력을 지원하지 않는 화면입니다');
      setter.call(priceInput,desired);
      priceInput.dispatchEvent(new Event('input',{bubbles:true}));
      priceInput.dispatchEvent(new Event('change',{bubbles:true}));
      if(money(priceInput.value)!==desired)return result('error','사이트 입력칸 가격 변경에 실패했습니다');
      buttons[0].click();
      return result('submitted','사이트 수정 제출 버튼 클릭. 실제 변경 여부는 별도로 검사해야 합니다',desired,1);
    })()
    """;
}
