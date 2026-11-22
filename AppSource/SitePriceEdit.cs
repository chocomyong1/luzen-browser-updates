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
    public double X { get; set; }
    public double Y { get; set; }
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
        string expectedTitle = "", bool nativeSingleApproval = false,
        bool trustedOneClick = false,
        bool nativeKeyboardPrice = false) =>
        EditFormScript.Replace("__ARG__", JsonSerializer.Serialize(new
        { number, oldPrice, newPrice, submit, expectedTitle, nativeSingleApproval,
            trustedOneClick,
            nativeKeyboardPrice }), StringComparison.Ordinal);

    // Read-only inspection of the authentic signed-in edit form. Never
    // return field values, inline source, cookies, query strings or tokens.
    public static string BuildSubmitBindingsInspectionScript(string number) =>
        SubmitBindingsInspectionScript.Replace("__ARG__", JsonSerializer.Serialize(new { number }),
            StringComparison.Ordinal);

    // The official BUY edit script intentionally returns false from its
    // FormChecker handler, then completes several asynchronous checks before
    // submitting natively. Never click unless the whole first-party chain
    // (checker, editor, sanitizer and AJAX helper) is initialized.
    public const string OfficialPriceSubmitReadinessScript = """
    (()=>{
      const result=(status,message)=>({status,message});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
         location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return result('error','공식 구매 수정폼이 아닙니다');
      const form=document.querySelector('form#frmBuy');
      if(!form||typeof form.onsubmit!=='function')
        return result('error','사이트 FormChecker 제출 연결이 준비되지 않았습니다');
      const missing=[];
      if(typeof window.ajaxRequest!=='function')missing.push('연락처 AJAX');
      if(typeof window.createLayerContent!=='function'||
         typeof window.createLayerContentProceed!=='function')missing.push('카테고리·제출 처리');
      if(typeof window.editor?.sendImages!=='function')missing.push('에디터 비동기 처리');
      if(typeof window.DOMPurify?.sanitize!=='function')missing.push('상세설명 동기화');
      return missing.length?result('error','사이트 제출 준비 미완료: '+missing.join(', ')):
        result('ready','사이트 비동기 검사·에디터·최종 제출 함수 연결 확인');
    })()
    """;

    // The site's original purchase editor synchronizes Quill to user_text
    // only on a text-change. An existing listing can display the description
    // while the hidden FormChecker field remains empty. That cancels the
    // first submit BEFORE the official AJAX validation starts, explaining
    // the repeated "submit canceled, no POST" reports. Mirror the site's
    // own sanitizer ONLY when its field is blank. The site's FormChecker
    // validates quantity relationships and minimum total as well as simple
    // required fields; verify those here before a click/checkpoint, and AGAIN
    // after the price input's native blur handlers run. Never alter title/ID.
    public const string OfficialPricePreflightScript = """
    (()=>{
      const result=(status,message)=>({status,message});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
        location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return result('error','사이트 구매 수정폼 이외 화면');
      const form=document.forms.frmBuy;
      const val=name=>String(form?.elements?.namedItem?.(name)?.value||'').trim();
      if(!form||form.getAttribute('id')!=='frmBuy')return result('error','수정폼 확인 불가');
      if(typeof form.onsubmit!=='function'||!form.checker)
        return result('error','사이트 FormChecker 초기화가 끝나지 않았습니다');
      if(!val('user_title'))return result('error','사이트 물품 제목칸이 비어 있습니다');
      // buy_re_reg.js FormChecker's original title character rules. This
      // checks site acceptance, never compares the user's saved title with
      // this listing or changes any title characters.
      const title=val('user_title');
      const safeChars=/[^a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣ,./%+~\s]/;
      const jamo=title.replace(safeChars,' ');
      if(safeChars.test(title)||/[ㄱ-ㅎㅏ-ㅣ]{2,}/.test(jamo)||/[,./%+~]{2,}/.test(jamo))
        return result('error','사이트 원본 제목에 FormChecker가 허용하지 않는 문자가 있습니다 (제목은 수정하지 않음)');
      const text=form.elements.namedItem('user_text');
      const root=window.editor?.root;
      const sanitize=window.DOMPurify?.sanitize;
      if(!text||!root||typeof sanitize!=='function')
        return result('error','사이트 상세설명 에디터를 확인할 수 없습니다');
      const html=String(root.innerHTML||'');
      const normalized=html==='<p><br></p>'?'':String(sanitize(html));
      if(!String(text.value||'').trim())
        return result('error',normalized.trim()
          ?'사이트 편집기가 상세설명을 제출 필드에 반영하지 않았습니다'
          :'사이트 상세설명 필드가 비어 있습니다');
      if(val('user_goods_type')!=='division')
        return result('error','현재 사이트 글이 분할 구매글이 아니어서 단가 입력 경로가 일치하지 않습니다');
      if(val('user_goods_type')==='division'){
        for(const [name,label] of [
          ['user_quantity_min_tmp','최소 구매수량'],
          ['user_quantity_max_tmp','최대 구매수량'],
          ['user_division_unit','분할단위']]){
          if(!val(name)||Number(val(name).replace(/,/g,''))===0)
            return result('error','사이트 '+label+' 입력값이 없어 FormChecker가 제출을 차단합니다');
        }
        const number=name=>Number(val(name).replace(/,/g,''));
        const isMoney=val('user_goods')==='money';
        const min=number(isMoney?'user_quantity_min':'user_quantity_min_tmp');
        const max=number(isMoney?'user_quantity_max':'user_quantity_max_tmp');
        const unit=number('user_division_unit');
        const price=number('user_division_price');
        if(![min,max,unit,price].every(n=>Number.isFinite(n)&&n>0))
          return result('error','사이트의 실제 최소·최대수량/분할단위/단가 중 0 또는 숫자가 아닌 값이 있습니다');
        if(unit>min)return result('error','사이트 분할단위가 최소수량보다 커서 FormChecker가 제출을 차단합니다');
        if(min>=max)return result('error','사이트 최소수량이 최대수량 이상이라 FormChecker가 제출을 차단합니다');
        // The first-party minimum total is initialPrice (1,000 KRW from
        // 2026-05-26), and its custom rule runs before contact AJAX.
        const minimum=typeof initialPrice==='number'&&Number.isFinite(initialPrice)
          ?initialPrice:null;
        if(minimum!==null&&min/unit*price<minimum)
          return result('error','변경 단가로 계산한 최소 구매금액이 사이트 기준 '+minimum+'원 미만입니다');
      }
      if(val('user_goods')!=='character'&&val('game_code')!=='281'&&!val('user_character'))
        return result('error','사이트 전달받을 캐릭터명이 비어 있어 FormChecker가 제출을 취소합니다');
      if(typeof _marketPriceBlocked==='boolean'&&_marketPriceBlocked)
        return result('error','사이트 시세 제한이 활성화되어 FormChecker가 제출을 차단합니다');
      const contact=val('user_contactA'),mobile=val('slctMobile_type');
      if(contact==='N'&&mobile==='N')
        return result('error','사이트 연락처 설정이 모두 비활성화되어 제출할 수 없습니다');
      return result('ready','사이트 FormChecker 수량·단가·제목 허용문자·필수값 및 상세설명 확인');
    })()
    """;

    // A separately invoked, read-only look at already referenced first-party
    // JavaScript assets. Only static .js GETs (no URL parameters) are made;
    // no form field values, request bodies, cookies, or listing data are read.
    public static string BuildSubmitValidatorSourceScript(string number) =>
        SubmitValidatorSourceScript.Replace("__ARG__", JsonSerializer.Serialize(new { number }),
            StringComparison.Ordinal);

    // WebView2 ExecuteScriptAsync does not reliably serialize the result of
    // an async IIFE. Start the read synchronously, then poll its completed
    // JSON-safe result. No website form or input is modified by either call.
    public const string SubmitValidatorSourceStatusScript =
        "window.__vantaPriceValidatorInspection || {status:'error',message:'사이트 스크립트 분석 상태가 사라졌습니다'}";

    public const string SubmitValidatorSourceScript = """
    (()=>{
      const a=__ARG__;
      const result=(status,message)=>({status,message});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
         location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return result('error','공식 구매 수정폼이 아닙니다');
      const form=document.querySelector('form#frmBuy');
      const button=form?.querySelector('button#ok_btn.buy_re_reg[type="submit"]');
      const price=form?.querySelector('input[name="user_division_price"]');
      if(!form||!button||!price||![...form.querySelectorAll('input[type="hidden"]')]
        .some(e=>String(e.value||'')===a.number))
        return result('error','수정폼의 등록번호·단가칸·사이트 제출 버튼을 확인하지 못했습니다');
      const paths=[...document.querySelectorAll('script[src]')].map(e=>{
        try{return new URL(e.src,location.href);}catch{return null;}
      }).filter(u=>u&&u.protocol==='https:'&&u.origin===location.origin&&
        /^\/[a-z0-9._/-]{1,130}\.js$/i.test(u.pathname)&&
        /(?:buy_re_reg|editor)(?:\.|\/|$)/i.test(u.pathname))
        .filter((u,i,all)=>all.findIndex(v=>v.pathname===u.pathname)===i).slice(0,3);
      const officialCdn=[...document.querySelectorAll('script[src]')].map(e=>{
        try{return new URL(e.src,location.href);}catch{return null;}
      }).filter(u=>u&&u.protocol==='https:'&&u.origin!==location.origin&&
        /(?:^|\.)itemmania\.com$/i.test(u.hostname)&&
        /^\/[a-z0-9._/-]{1,130}\.js$/i.test(u.pathname)&&
        /(?:buy_re_reg|editor)(?:\.|\/|$)/i.test(u.pathname)).slice(0,4);
      const lines=['[VANTA 사이트 구매 수정 JS 검증 코드 — 읽기 전용]',
        '수정폼·등록번호·가격칸: 일치 (실제 값 비공개)',
        '사이트 스크립트 '+paths.length+'개 검사 (공식 동일 출처, 쿼리 없는 .js GET만)'];
      const marker=/frmBuy|ok_btn|user_division_price|buy_re_reg_ok|onsubmit|preventDefault|\.submit\s*\(|return\s+false|re_reg/i;
      const sanitize=s=>String(s||'').replace(/"[^"]{0,400}"|'[^']{0,400}'|`[^`]{0,400}`/g,
          literal=>/^['"](?:#?frmBuy|#?ok_btn|user_division_price|submit|click)['"]$/.test(literal)
            ?literal:'[문자열]')
        .replace(/https?:\/\/[^\s'"<>]+/gi,'[URL]')
        .replace(/[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}/gi,'[이메일]')
        .replace(/\b\d{8,}\b/g,'[숫자가림]').replace(/[\x00-\x1f]/g,' ');
      if(typeof window.premiumLayerOpen==='function'){
        const premiumSource=Function.prototype.toString.call(window.premiumLayerOpen);
        lines.push('추가 확인 함수: '+sanitize(premiumSource).slice(0,3200));
        const selectors=[...premiumSource.matchAll(/['"]([#.][a-zA-Z][\w-]{0,60})['"]/g)]
          .map(match=>match[1]);
        if(selectors.length)lines.push('정적 선택자: '+[...new Set(selectors)].join(', '));
        const fields=[...premiumSource.matchAll(/getElementById\(['"]([\w-]+)['"]\)/g)]
          .map(match=>match[1]);
        const layer=fields.length===3?document.getElementById(fields[2]):null;
        lines.push('정적 선택자: '+fields.join(', '));
        if(layer){
          const controls=[...layer.querySelectorAll('a,button,input[type="button"],input[type="submit"]')]
            .slice(0,16).map(e=>[e.id,e.getAttribute('class'),e.innerText||e.value||'']
              .map(x=>String(x||'').replace(/[^\w\s가-힣-]/g,'').trim().slice(0,50)).join('/'));
          lines.push('정적 선택자: 프리미엄 레이어 제어 '+controls.join(' | '));
        }
      }
      window.__vantaPriceValidatorInspection=result('loading','사이트 정적 JS를 읽고 있습니다');
      // Return a plain object to WebView2 immediately. The asynchronous GETs
      // update a separate, polled result after the browser completes them.
      (async()=>{
      for(const url of paths){
        lines.push('파일: '+url.pathname);
        try{
          const response=await fetch(url.origin+url.pathname,
            {method:'GET',credentials:'same-origin',cache:'force-cache',redirect:'error'});
          if(!response.ok||!(/javascript|text\/plain|octet-stream/i).test(response.headers.get('content-type')||'')){
            lines.push('코드 조회 불가: HTTP '+response.status);continue;
          }
          const length=Number(response.headers.get('content-length')||0);
          if(length>350000){lines.push('코드가 350KB를 넘어 읽지 않음');continue;}
          const source=await response.text();
          if(source.length>350000){lines.push('코드가 350KB를 넘어 분석하지 않음');continue;}
          lines.push('코드 크기: '+source.length+'자');
          const sourceLines=source.split(/\r?\n/),matching=[];
          for(let i=0;i<sourceLines.length&&matching.length<9;i++){
            const line=sourceLines[i];
            if(!marker.test(line))continue;
            // Static files may be minified onto a single huge line. Only
            // small windows around unique validation markers are copied.
            let start=0;
            while(matching.length<9){
              const suffix=line.slice(start),hit=marker.exec(suffix);
              if(!hit)break;
              const pos=start+hit.index;
              matching.push('L'+(i+1)+': '+sanitize(line.slice(Math.max(0,pos-95),pos+175)));
              start=pos+Math.max(hit[0].length,1);
            }
          }
          lines.push(...(matching.length?matching:['관련 검증 표현 발견 안 됨']));
        }catch(e){lines.push('정적 JS 읽기 실패: '+sanitize(e?.name||'오류').slice(0,35));}
      }
      if(!paths.length)lines.push('같은 출처 JS 경로를 찾지 못함 · 외부 CDN 출처일 수 있습니다');
      for(const url of officialCdn)lines.push('공식 CDN 경로 (GET하지 않음): '+url.hostname+url.pathname);
      lines.push('사이트 제출·클릭·가격 입력은 실행하지 않았습니다. 정적 스크립트 GET만 수행했습니다.');
      window.__vantaPriceValidatorInspection=result('inspected',lines.join('\n').slice(0,6500));
      })().catch(e=>{
        window.__vantaPriceValidatorInspection=result('error',
          '정적 JS 비동기 분석 오류: '+sanitize(e?.name||'오류').slice(0,35));
      });
      return result('started','공식 정적 JS 읽기 시작');
    })()
    """;

    public const string SubmitBindingsInspectionScript = """
    (()=>{
      const a=__ARG__;
      const result=(status,message)=>({status,message});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
         location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return result('error','공식 구매 재등록 수정폼이 아닙니다');
      const form=document.querySelector('form#frmBuy');
      const button=form?.querySelector('button#ok_btn.buy_re_reg[type="submit"]');
      const price=form?.querySelector('input[name="user_division_price"]');
      if(!form||!button||!price||![...form.querySelectorAll('input[type="hidden"]')]
        .some(e=>String(e.value||'')===a.number))
        return result('error','수정폼·등록번호·가격칸·제출 버튼을 함께 확인하지 못했습니다');
      const safe=s=>String(s||'').replace(/\d{8,}/g,'[번호가림]')
        .replace(/[^\w.$<>가-힣-]/g,'').slice(0,42)||'익명';
      const functionNames=s=>[...String(s||'').matchAll(/\b([A-Za-z_$][\w$]{2,45})\s*\(/g)]
        .map(m=>m[1]).filter(n=>!['function','switch','while','catch','return'].includes(n))
        .slice(0,6).map(safe).join(',')||'미확인';
      const flags=s=>{
        s=String(s||'');
        return ['return-false','preventDefault','submit','ajax','fetch','confirm','alert','validation']
          .filter(f=>f==='return-false'?/\breturn\s*(?:false|!1|0)\b/.test(s):
            f==='validation'?/valid|check|verify|검증|확인/.test(s):
            new RegExp('\\b'+f+'\\b','i').test(s)).join(',')||'없음';
      };
      const fnSummary=fn=>{
        if(typeof fn!=='function')return '없음';
        const source=Function.prototype.toString.call(fn);
        return '함수='+safe(fn.name)+'; 신호='+flags(source)+
          '; 호출='+functionNames(source)+'; 코드길이='+source.length;
      };
      const fnExcerpt=fn=>{
        if(typeof fn!=='function')return '없음';
        const source=Function.prototype.toString.call(fn);
        const index=Math.max(0,source.search(/return\s+false|preventDefault|submit|action|validate|check/i));
        return source.slice(Math.max(0,index-70),index+260)
          .replace(/"[^"]{0,400}"|'[^']{0,400}'|`[^`]{0,400}`/g,'[문자열]')
          .replace(/https?:\/\/[^\s'"<>]+/gi,'[URL]')
          .replace(/\b\d{8,}\b/g,'[숫자가림]')
          .replace(/[\x00-\x1f]/g,' ').slice(0,320);
      };
      const attrSummary=(el,name)=>{
        const raw=el?.getAttribute?.(name)||'';
        return raw?'호출='+functionNames(raw)+'; 신호='+flags(raw):'없음';
      };
      const lines=[
        '[VANTA 구매 수정폼 제출 연결 검사 — 읽기 전용]',
        '페이지/폼/등록번호 확인: 일치 (등록번호와 입력값은 복사하지 않음)',
        '폼: '+String(form.method||'-').toUpperCase()+' / '+String(form.enctype||'-').slice(0,45),
        '폼 action 경로: '+(new URL(form.action,location.href)).pathname.slice(0,100),
        '폼 onsubmit 속성: '+attrSummary(form,'onsubmit'),
        '폼 onsubmit 함수: '+fnSummary(form.onsubmit),
        '폼 DOM0 코드 조각(문자열 제외): '+fnExcerpt(form.onsubmit),
        '버튼 onclick 속성: '+attrSummary(button,'onclick'),
        '버튼 onclick 함수: '+fnSummary(button.onclick),
        '버튼 코드 조각(문자열 제외): '+fnExcerpt(button.onclick),
        '버튼 disabled: '+(button.disabled?'예':'아니오')+
          '; 폼 HTML 유효성 오류 필드 수: '+form.querySelectorAll(':invalid').length,
        '사이트 제목 수정칸 존재: '+(!!form.querySelector('input[name="user_title"],textarea[name="user_title"]')?'예':'아니오')
      ];
      const jq=typeof window.jQuery==='function'?window.jQuery:null;
      if(jq&&typeof jq._data==='function'){
        for(const [label,target] of [['버튼',button],['폼',form],['문서',document],['창',window]]){
          let events={};
          try{events=jq._data(target,'events')||{};}catch{}
          for(const kind of ['click','submit','change','input','keyup']){
            const handlers=Array.isArray(events[kind])?events[kind]:[];
            for(const h of handlers.slice(0,8))
              lines.push('jQuery '+label+' '+kind+' / '+(h.selector?'위임':'직접')+
                ' / '+fnSummary(h.handler)+' / 코드 일부:'+fnExcerpt(h.handler));
          }
        }
      }else lines.push('jQuery 내부 이벤트 목록: 조회 불가 (사이트가 다른 방식으로 연결했을 수 있음)');
      const relevantScripts=[...document.querySelectorAll('script[src]')]
        .map(e=>{try{const u=new URL(e.src,location.href);return u.hostname===location.hostname?
          u.pathname:'';}catch{return '';}})
        .filter(s=>/^\/[a-z0-9._/-]{1,130}\.js$/i.test(s)&&
          !/\d{8,}/.test(s)&&/buy|re.?reg|edit|modify|item|goods|myroom/i.test(s))
        .slice(0,12);
      lines.push('관련 사이트 스크립트 경로(쿼리 제외): '+(relevantScripts.join(',')||'확인 안 됨'));
      lines.push('이 검사는 버튼 클릭·폼 제출·가격 입력·네트워크 요청을 실행하지 않았습니다.');
      return result('inspected',lines.join('\n').slice(0,5900));
    })()
    """;

    public const string VerifyNativePriceInputScript = """
    (()=>window.__luzenPriceNativeProbe?.canStartTyping?.()===true)()
    """;

    public const string VerifyNativePriceFocusScript = """
    (()=>window.__luzenPriceNativeProbe?.priceInput===document.activeElement&&
      window.__luzenPriceNativeProbe?.canType?.()===true)()
    """;

    // A site's input mask can defeat Ctrl+A followed by per-digit input.
    // This fall-back only selects the *same* already-audited price field.
    // No click on the submit button and no site request can occur here.
    public const string SelectNativePriceForReplacementScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe;
      const result=(status,message='')=>({status,message});
      if(!p||!p.canType?.()||p.clickSeen||p.submitSeen||p.titleChanged())
        return result('error','가격 재입력 전에 수정폼·제목·등록번호 검증에 실패했습니다');
      const e=p.priceInput;
      if(String(e.type||'').toLowerCase()!=='text'&&String(e.type||'').toLowerCase()!=='tel')
        return result('error','이 가격 입력칸의 형태에서는 안전한 전체 선택을 지원하지 않습니다');
      e.focus();
      try{e.select();}catch{return result('error','가격 입력칸 전체 선택 실패');}
      if(document.activeElement!==e||e.selectionStart!==0||e.selectionEnd!==e.value.length)
        return result('error','기존 가격의 모든 숫자를 선택하지 못해 재입력하지 않습니다');
      return result('retry-ready','같은 가격 입력칸에서 기존 금액 전체 선택 확인 · 사이트 제출 없음');
    })()
    """;

    // Run only after the host has typed the desired price into the site's
    // verified input using actual browser keyboard events. The original
    // title, listing ID and every non-price field are checked once more.
    public const string FinalizeNativePriceScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe;
      const result=(status,message='',x=0,y=0)=>({status,message,x,y});
      if(!p||!p.canType?.())return result('error','가격 입력 중 사이트 수정폼이 변경되어 제출하지 않았습니다');
      if(p.money(p.priceInput.value)!==p.desired)
        return result('input-mismatch','사이트 가격 입력칸의 실제 숫자: '+
          (p.money(p.priceInput.value).slice(0,18)||'빈칸')+
          ' / 요청 숫자: '+p.desired+' (입력 형태: '+String(p.priceInput.type||'미확인').slice(0,18)+')');
      if(p.titleChanged())return result('error','가격 입력 중 원본 제목이 변형돼 제출하지 않았습니다');
      const changed=p.others.filter(o=>String(o.element.value??'')!==o.value);
      const safePriceField=o=>{
        const e=o.element,name=String(e.name||e.id||'');
        return String(e.type||'').toLowerCase()==='hidden'&&
          /(?:price|amount|total|sum|fee|cost|charge|money|payment)/i.test(name)&&
          /^[-+\d,.]*$/.test(String(e.value||''));
      };
      if(changed.some(o=>!safePriceField(o)))
        return result('error','사이트가 가격 외 다른 폼 값을 변경하여 제출하지 않았습니다. 영향받은 필드: '+
          changed.filter(o=>!safePriceField(o)).slice(0,4).map(o=>
            String(o.element.name||o.element.id||'이름 없음').slice(0,40)).join(','));
      p.fieldsBeforeSubmit=p.others.map(({element})=>({element,value:String(element.value??''),
        checked:element.checked}));
      const button=p.button;
      button.scrollIntoView?.({block:'center',inline:'center'});
      const rect=button.getBoundingClientRect?.();
      const x=rect?(rect.left+rect.right)/2:NaN;
      const y=rect?(rect.top+rect.bottom)/2:NaN;
      const point=Number.isFinite(x)&&Number.isFinite(y)&&x>0&&y>0&&
        x<window.innerWidth&&y<window.innerHeight?document.elementFromPoint?.(x,y):null;
      if(!point||!(point===button||button.contains?.(point)))
        return result('error','사이트 재등록 버튼 위치를 확인하지 못해 제출하지 않았습니다');
      p.canClick=()=>p.canType()&&p.money(p.priceInput.value)===p.desired&&
        !p.titleChanged()&&p.others.every(o=>String(o.element.value??'')===o.value||safePriceField(o))&&
        (document.elementFromPoint(x,y)===button||button.contains?.(document.elementFromPoint(x,y)));
      button.addEventListener('click',e=>{
        const otherFieldsUnchanged=p.others.every(o=>
          String(o.element.value??'')===o.value||safePriceField(o));
        if(!p.canType()||p.money(p.priceInput.value)!==p.desired||
          p.titleChanged()||!otherFieldsUnchanged){
          p.guardBlocked=true;
          e.preventDefault();e.stopImmediatePropagation();
          window.alert('가격 외 다른 입력값이 변경되었거나 등록번호·가격을 확인할 수 없습니다. 사이트 재등록을 중단했습니다.');
          return;
        }
        p.clickSeen=true;p.clickTrusted=e.isTrusted===true;
      },{capture:true});
      // The site attaches an anonymous DOM0 onsubmit handler. Its native
      // event functions MUST NOT be replaced, proxied or wrapped: that can
      // interfere with the form's own JavaScript submit validation. Observe
      // the event and its action pathname only, without altering anything.
      const observeSiteSubmit=e=>{
        if(e.target!==p.form)return;
        p.submitCaptureSeen=true;
        p.dom0SubmitBound=typeof p.form.onsubmit==='function';
        try{p.submitActionAtEvent=new URL(p.form.action,location.href).pathname;}
        catch{p.submitActionAtEvent='불명';}
      };
      window.addEventListener?.('submit',observeSiteSubmit,{capture:true,once:true});
      const protectTitleAtSubmit=e=>{
        if(e.target!==p.form)return;
        p.submitSeen=true;p.submitEvent=e;
        if(p.titleChanged()||p.money(p.priceInput.value)!==p.desired||
          p.others.some(o=>String(o.element.value??'')!==o.value&&!safePriceField(o))){
          p.guardBlocked=true;e.preventDefault();e.stopImmediatePropagation();
        }
      };
      // Capture guards site click normalization; a second, late bubble
      // guard catches a site's own form-submit handler rewriting the title.
      // The official FormChecker cancels its first submit on purpose. A
      // visible editor may be retried after correcting a site alert, so
      // preserve the title/price guard for every later submit event too.
      p.form.addEventListener('submit',protectTitleAtSubmit,{capture:true});
      p.form.addEventListener('submit',protectTitleAtSubmit,{capture:false});
      return result('armed','사이트 가격칸의 실제 키보드 입력 및 원본 제목 확인 · 재등록 단건 클릭 준비',x,y);
    })()
    """;

    // Read-only signal used when closing the visible official editor. A
    // canceled click does not count as a price submission attempt.
    public const string NativePriceButtonClickedScript = """
    (()=>window.__luzenPriceNativeProbe?.clickSeen===true)()
    """;

    // Passive error counters installed BEFORE the original first-party form
    // is clicked. No event is canceled, no site function is replaced, and no
    // JavaScript exception message or stack (which could contain PII) is read.
    public const string InstallSiteSubmitErrorProbeScript = """
    (()=>{
      if(window.__vantaPriceSiteErrors)return true;
      const state={script:0,promise:0};
      window.__vantaPriceSiteErrors=state;
      window.addEventListener('error',e=>{
        if(e && typeof e.message==='string')state.script++;
      });
      window.addEventListener('unhandledrejection',()=>{state.promise++;});
      return true;
    })()
    """;

    // Only reads branch indicators from the ACTUAL official edit page when
    // its contact AJAX completed but no final POST followed. Never exposes
    // contact fields, premium pricing, dialog text or a listing's form values.
    public const string PostContactBlockerProbeScript = """
    (()=>{
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
         location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return '공식 수정 화면 아님';
      const penalty=document.getElementById('user_premium_penalty');
      const extra=penalty?(String(penalty.value)==='1'?'적용':'미적용'):'필드 없음';
      const elements=[...document.querySelectorAll('[role="dialog"], [aria-modal="true"], .ui-dialog, .modal')];
      const visible=elements.filter(e=>{
        const r=e.getBoundingClientRect(),s=getComputedStyle(e);
        return r.width>0&&r.height>0&&s.visibility!=='hidden'&&s.display!=='none';
      }).length;
      const errors=window.__vantaPriceSiteErrors;
      return '추가 확인 분기 표시:'+extra+
        '; 보이는 대화 레이어:'+visible+
        '; 사이트 스크립트 예외:'+(errors?.script??'미측정')+
        '; 비동기 예외:'+(errors?.promise??'미측정')+
        '; 프리미엄 처리 함수:'+(typeof window.premiumLayerOpen==='function'?'있음':'없음');
    })()
    """;

    public const string OfficialPremiumConfirmationScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe;
      const layer=document.getElementById('dvPremium');
      const buttons=layer?.querySelectorAll('#premium_btn');
      const button=buttons?.length===1?buttons[0]:null;
      if(!p?.clickSeen||!p.submitSeen||p.guardBlocked||!p.canType?.()||
         p.money(p.priceInput.value)!==p.desired||
         document.getElementById('user_premium_penalty')?.value!=='1'||
         !button||button.disabled||button.innerText.trim()!=='확인'||
         !button.getClientRects().length||getComputedStyle(button).visibility!=='visible')
        return false;
      // The site's confirmation may apply premium settings. Keep the
      // existing settings and let the user make the site's own decision.
      const layerFields=[...layer.querySelectorAll('input,select')]
        .map(element=>({element,value:String(element.value??''),checked:element.checked}));
      button.addEventListener('click',e=>{
        if(p.canType()&&p.money(p.priceInput.value)===p.desired&&
           [...p.fieldsBeforeSubmit,...layerFields].every(({element,value,checked})=>
             String(element.value??'')===value&&element.checked===checked))return;
        e.preventDefault();
        e.stopImmediatePropagation();
        window.alert('가격 외 입력값이 변경되었습니다. 사이트의 기존 설정을 유지해 주세요.');
      },{capture:true});
      return true;
    })()
    """;

    // Alternative to repeatedly pressing the site's re-registration button:
    // after the official contact check ACCEPTS, inspect the actual premium
    // layer for an explicit FREE/ORDINARY continuation. Never submit a form,
    // synthesize a network request, select a paid option or dismiss security.
    // The host uses a real browser pointer, guarded again immediately before
    // pressing, and the same exact-ID/price signed-in readback afterwards.
    public const string FindOfficialFreeContinuationScript = """
    (()=>{
      const result=(status,message='',x=0,y=0)=>({status,message,x,y});
      if(location.protocol!=='https:'||!/(^|\.)itemmania\.com$/i.test(location.hostname)||
         location.pathname.toLowerCase()!=='/myroom/buy/buy_re_reg.html')
        return result('blocked','공식 구매 수정 화면 아님');
      const p=window.__luzenPriceNativeProbe;
      if(!p||!p.clickSeen||!p.submitSeen||!p.canType?.()||p.titleChanged?.()||
         p.money(p.priceInput.value)!==p.desired||p.guardBlocked)
        return result('blocked','원본 등록번호·제목·가격 또는 첫 제출 상태 불일치');
      if(String(document.getElementById('user_premium_penalty')?.value)!=='1')
        return result('not-applicable','프리미엄 분기 대상 아님');
      const visible=e=>{
        if(!e?.isConnected||e.disabled)return false;
        const r=e.getBoundingClientRect?.(),s=getComputedStyle(e);
        return !!r&&r.width>0&&r.height>0&&s.visibility!=='hidden'&&
          s.display!=='none'&&s.pointerEvents!=='none';
      };
      const text=e=>String(e?.innerText||e?.value||e?.textContent||'').replace(/\s+/g,'').trim();
      const free=/^(?:무료등록(?:하기)?|일반등록(?:하기)?|프리미엄없이등록(?:하기)?|유료서비스이용안함)$/;
      const candidates=[...document.querySelectorAll(
          'button,input[type="button"],input[type="submit"],a[role="button"]')]
        .filter(visible).filter(e=>free.test(text(e)))
        .map(button=>({button,layer:button.closest(
          '[role="dialog"],.layer_pop,.layer_wrap,.modal,.popup,[id*="premium" i],[class*="premium" i]')}))
        .filter(({button,layer})=>layer&&layer!==p.form&&
          !p.form.contains(layer)&&visible(layer)&&
          /프리미엄|부가서비스|유료등록|무료등록/.test(String(layer.innerText||'')));
      if(candidates.length===0)return result('absent','무료·일반 진행 버튼 없음');
      if(candidates.length!==1)return result('ambiguous','무료·일반 진행 버튼 중복');
      const {button,layer}=candidates[0];
      const label=text(button);
      const content=String(layer.innerText||'').replace(/\s+/g,'');
      // A free button cannot authorize a modal that is already charging for
      // another selected service. In that case ask for human consent instead.
      const amount=content.match(/(?:총결제금액|결제예정금액|결제금액)[:：]?([\d,]+)원/);
      if(amount&&Number(amount[1].replace(/,/g,''))!==0)
        return result('paid','현재 추가 서비스 결제 금액이 0원이 아님');
      button.scrollIntoView?.({block:'center',inline:'center'});
      const r=button.getBoundingClientRect();const x=(r.left+r.right)/2,y=(r.top+r.bottom)/2;
      const hit=Number.isFinite(x)&&Number.isFinite(y)&&x>0&&y>0&&
        x<innerWidth&&y<innerHeight?document.elementFromPoint?.(x,y):null;
      if(!hit||!(hit===button||button.contains(hit)))
        return result('covered','무료·일반 진행 버튼이 화면에서 가려짐');
      window.__vantaFreePremiumCandidate={button,layer,label,desired:p.desired};
      return result('free-ready','공식 프리미엄 안내의 무료·일반 진행 버튼 1개 검증',x,y);
    })()
    """;

    public const string VerifyOfficialFreeContinuationScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe,c=window.__vantaFreePremiumCandidate;
      if(!p||!c||!p.clickSeen||!p.submitSeen||!p.canType?.()||p.guardBlocked||
         p.titleChanged?.()||p.money(p.priceInput.value)!==p.desired||
         c.desired!==p.desired||!c.button?.isConnected||c.button.disabled||
         !c.layer?.contains(c.button)||!c.layer?.isConnected||
         String(document.getElementById('user_premium_penalty')?.value)!=='1')return false;
      const label=String(c.button.innerText||c.button.value||c.button.textContent||'')
        .replace(/\s+/g,'').trim();
      if(label!==c.label)return false;
      const r=c.button.getBoundingClientRect(),x=(r.left+r.right)/2,y=(r.top+r.bottom)/2;
      const hit=document.elementFromPoint?.(x,y);
      return x>0&&y>0&&x<innerWidth&&y<innerHeight&&
        !!hit&&(hit===c.button||c.button.contains(hit));
    })()
    """;

    // Read-only result of the ONE native browser-input trial; does not inspect
    // field values, credentials or request contents and never invokes submit.
    public const string SubmitProbeScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe;
      if(!p)return '사이트 제출 진단 신호 없음';
      const cancellation=p.submitEvent?.defaultPrevented===true;
      const titleChanged=p.titleChanged?.()===true;
      const invalid=[...p.form.querySelectorAll(':invalid')].slice(0,5)
        .map(e=>String(e.name||e.id||'이름 없음').slice(0,35));
      // FormChecker uses JavaScript validation (not :invalid), and often
      // focuses the first failing control. Record only its field NAME,
      // never its value, to diagnose a canceled submit before contact AJAX.
      const active=typeof document==='undefined'?null:document.activeElement;
      const focused=active?.closest?.('form')===p.form
        ?String(active.name||active.id||'이름 없음').replace(/[^a-zA-Z0-9_-]/g,'').slice(0,35)
        :'없음';
      const safePath=s=>String(s||'unknown').replace(/\d{8,}/g,'ID')
        .replace(/[^a-zA-Z0-9._/-]/g,'').slice(0,110);
      const currentAction=(()=>{try{return new URL(p.form.action,location.href).pathname;}
        catch{return '불명';}})();
      return '버튼 입력:'+ (p.clickSeen?(p.clickTrusted?'사용자형':'합성'):'감지 안 됨')+
        '; 키업/입력/변경/포커스이탈:'+[
          p.keyUps||0,p.inputEvents||0,p.changeEvents||0,p.blurEvents||0].join('/')+
        '; 제출 이벤트:'+(p.submitSeen?'감지':'미감지')+
        '; 제출 캡처:'+(p.submitCaptureSeen?'감지':'미감지')+
        '; 사이트 제출 취소:'+(cancellation?'예':'아니오/미확인')+
        '; 초기 취소 해석:FormChecker 비동기 검증에서는 정상 가능(최종 POST 별도 확인)'+
        '; 취소 호출:비개입 관찰(호출 함수 추적 안 함)'+
        '; DOM0 onsubmit:'+(p.dom0SubmitBound?'연결':'없음/미확인')+
        '; 제출 전후 폼 action:'+safePath(p.submitActionAtEvent)+'→'+safePath(currentAction)+
        '; 제목 보호 차단:'+(p.guardBlocked?'예':'아니오')+
        '; 현재 제목 변형:'+(titleChanged?'예':'아니오')+
        '; 사이트 검사 후 포커스 필드:'+focused+
        '; HTML 유효성 오류 필드:'+(invalid.join(',')||'없음');
    })()
    """;

    // Last-moment read-only check after mouse move but BEFORE mouse press.
    public const string VerifyNativeClickScript = """
    (()=>window.__luzenPriceNativeProbe?.canClick?.()===true)()
    """;

    // The footer can move after the native mouse enters the page. Refresh
    // coordinates from the same verified submit button, never from a label.
    // This script is read-only: no click, price edit, or POST occurs here.
    public const string RefreshNativeClickTargetScript = """
    (()=>{
      const p=window.__luzenPriceNativeProbe;
      const result=(status,message='',x=0,y=0)=>({status,message,x,y});
      if(!p||p.clickSeen||p.submitSeen||!p.canType?.())
        return result('error','등록번호·제목 검증 실패 또는 이미 클릭됨');
      if(p.money(p.priceInput.value)!==p.desired||p.titleChanged())
        return result('error','가격 또는 원본 제목이 변경됐습니다');
      const safePriceField=o=>{
        const e=o.element,name=String(e.name||e.id||'');
        return String(e.type||'').toLowerCase()==='hidden'&&
          /(?:price|amount|total|sum|fee|cost|charge|money|payment)/i.test(name)&&
          /^[-+\d,.]*$/.test(String(e.value||''));
      };
      if(p.others.some(o=>String(o.element.value??'')!==o.value&&!safePriceField(o)))
        return result('error','가격 외 수정폼 필드가 변경됐습니다');
      const button=p.button;
      if(!button?.isConnected||button.disabled||button.closest?.('form')!==p.form)
        return result('error','검증된 재등록 버튼이 폼에서 사라졌습니다');
      const rect=button.getBoundingClientRect?.();
      const x=rect?(rect.left+rect.right)/2:NaN;
      const y=rect?(rect.top+rect.bottom)/2:NaN;
      if(!Number.isFinite(x)||!Number.isFinite(y)||x<=0||y<=0||
         x>=window.innerWidth||y>=window.innerHeight)
        return result('error','재등록 버튼이 브라우저 화면 밖으로 이동했습니다');
      const isButtonPoint=()=>{
        const hit=document.elementFromPoint?.(x,y);
        return !!hit&&(hit===button||button.contains?.(hit));
      };
      if(!isButtonPoint())return result('error','재등록 버튼이 다른 요소에 가려져 있습니다');
      p.canClick=()=>p.canType()&&p.money(p.priceInput.value)===p.desired&&
        !p.titleChanged()&&p.others.every(o=>String(o.element.value??'')===o.value||safePriceField(o))&&
        isButtonPoint();
      return result('click-ready','등록번호·제목·가격·재등록 버튼 위치 재검증 완료',x,y);
    })()
    """;

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
      const result=(status,message='',price='',candidates=0,x=0,y=0)=>({status,message,price,candidates,x,y});
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
        const alias=/^(price|goods_price|sale_price|buy_price|sell_price|trade_price|goodsPrice|buyPrice|sellPrice|user_division_price)$/i;
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
      // the amount. Never send it unless the title captured from the exact
      // signed-in OWN-list row survives intact. Saved local titles may be old.
      // Do not "repair" a corrupted title by guessing from quantity/level.
      const cleanTitle=s=>String(s||'').normalize('NFC').replace(/\s+/g,' ').trim();
      // The edit form sometimes changes only spacing ("9 레벨" vs
      // "9레벨"). That is display formatting, not permission to replace,
      // repair, strip a quantity range, or add/remove ANY actual character.
      // Keep all digits and punctuation; a doubled 9 or 10 MUST still fail.
      const titleSignature=s=>cleanTitle(s).replace(/\s/g,'');
      const titleName=/^(?:title|subject|goods_?title|item_?title|user_?title|goods_?subject|item_?subject|user_?subject|goods_?name|item_?name|user_?item_?name)$/i;
      const titleInputs=form?[...form.querySelectorAll('input,textarea')].filter(e=>
        titleName.test(String(e.getAttribute?.('name')||e.name||e.getAttribute?.('id')||''))):[];
      const originalTitle=cleanTitle(a.expectedTitle);
      const siteTitle=titleInputs.length===1?cleanTitle(titleInputs[0].value):'';
      const titlePreserved=originalTitle.length>=3&&titleInputs.length===1&&
        titleSignature(siteTitle)===titleSignature(originalTitle);
      const displayTitle=s=>String(s||'').replace(/[\r\n\t]/g,' ').trim().slice(0,90);
      const siteSubmit=isAuditedBuyForm?[...form.querySelectorAll('button[type="submit"]')].filter(e=>
        visible(e)&&attr(e,'id')==='ok_btn'&&attr(e,'class').split(/\s+/).includes('buy_re_reg')&&
        String(e.innerText||e.textContent||'').replace(/\s+/g,'')==='재등록'):[];
      // All of path, form, old site price, hidden listing ID, and the EXACT
      // native submit control must agree. A character-name save cannot pass.
      if(formOwnedId&&siteSubmit.length===1){
        if(!titlePreserved)return result('error',titleInputs.length!==1?
          '가격 수정은 제목까지 재등록하므로 제목 입력칸을 유일하게 확인할 때까지 제출을 차단합니다':
          '실제 제목 내용이 달라 가격 제출을 막았습니다.\n'+
          '사이트 수정폼: '+displayTitle(siteTitle)+'\n'+
          '내 등록목록 제목: '+displayTitle(originalTitle)+'\n'+
          '원래 제목과 다를 때는 자동으로 제목을 고치거나 가격을 제출하지 않습니다.');
        // The scheduler and batch paths never get this explicit one-item
        // approval. Never synthesize fetch(FormData(form)) or form.submit():
        // those bypass the site's own validation/normalization handlers.
        if(a.nativeSingleApproval===true){
          if(!a.submit)return result('ready','사이트 기본 재등록 버튼 확인 · 단건 사용자 승인 대기',old,1);
          // A JavaScript element.click() creates an untrusted click event.
          // Prepare only the audited form here. The host sends ONE native
          // browser mouse click after persisting the review checkpoint.
          if(a.trustedOneClick!==true)return result('blocked','가격 제출은 검증된 실제 브라우저 클릭 경로로만 허용합니다',old,1);
          if(a.nativeKeyboardPrice!==true)
            return result('blocked','실제 사이트 키보드로 가격칸을 입력하는 방식만 허용합니다',old,1);
          // Snapshot all other fields. Site-controlled numeric price totals
          // may update during real typing; identity/title/quantity changes
          // will still prevent the submit.
          const others=[...form.querySelectorAll('input,textarea,select')]
            .filter(e=>e!==priceInput)
            .map(e=>({element:e,value:String(e.value??'')}));
          const button=siteSubmit[0];
          priceInput.scrollIntoView?.({block:'center',inline:'center'});
          const rect=priceInput.getBoundingClientRect?.();
          const x=rect?(rect.left+rect.right)/2:NaN;
          const y=rect?(rect.top+rect.bottom)/2:NaN;
          const point=Number.isFinite(x)&&Number.isFinite(y)&&x>0&&y>0&&
            x<window.innerWidth&&y<window.innerHeight?document.elementFromPoint?.(x,y):null;
          if(!point||!(point===priceInput||priceInput.contains?.(point))){
            return result('error','사이트 가격 입력칸의 화면 위치를 검증하지 못해 제출하지 않았습니다',old,1);
          }
          const probe={clickSeen:false,clickTrusted:false,submitSeen:false,
            guardBlocked:false,submitEvent:null,submitCaptureSeen:false,
            dom0SubmitBound:false,submitActionAtEvent:'',
            keyUps:0,inputEvents:0,changeEvents:0,blurEvents:0,
            form,button,priceInput,others,desired,money,
            titleChanged:()=>titleSignature(titleInputs[0].value)!==titleSignature(originalTitle),
            canType:()=>location.pathname.toLowerCase()==='/myroom/buy/buy_re_reg.html'&&
              button.isConnected&&priceInput.isConnected&&!priceInput.disabled&&!priceInput.readOnly&&
              titleSignature(titleInputs[0].value)===titleSignature(originalTitle)&&
              [...form.querySelectorAll('input[type="hidden"]')].some(e=>String(e.value||'')===a.number),
            canStartTyping:()=>location.pathname.toLowerCase()==='/myroom/buy/buy_re_reg.html'&&
              button.isConnected&&priceInput.isConnected&&
              titleSignature(titleInputs[0].value)===titleSignature(originalTitle)&&
              document.elementFromPoint(x,y)&&
              (document.elementFromPoint(x,y)===priceInput||priceInput.contains?.(document.elementFromPoint(x,y)))};
          for(const [name,key] of [['keyup','keyUps'],['input','inputEvents'],
            ['change','changeEvents'],['blur','blurEvents']])
            priceInput.addEventListener?.(name,()=>{probe[key]++;},{passive:true});
          window.__luzenPriceNativeProbe=probe;
          // No price or submit action occurs in this script. The host types
          // through real browser keyboard events before the final readback.
          return result('input-ready','사이트 가격 입력칸 검증 · 실제 키보드 입력 준비 (제출 없음)',old,1,x,y);
        }
        return result('blocked','제목 보호 모드: 예약·일괄 가격 자동 제출 중지',old,1);
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
