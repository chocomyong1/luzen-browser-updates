using System;
using System.Text.Json;

namespace LuzenBrowserReplica;

// Account-list reposting is a separate pathway from reposting a single detail URL.
// The click is constrained to the ONE row with both this item's ID and title.
// Nothing in this script clicks a site-wide toolbar, pays fees or solves checks.
internal sealed class OwnedListRepostResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    // Site-rendered registration time for this ONE listing row, not browser clock.
    public string Stamp { get; set; } = "";
    // Whether the verified site's own function or its row button was used.
    public string Method { get; set; } = "";
    // Read-only diagnostics: never used as permission to submit a repost.
    public int ScannedRows { get; set; }
    public int NumberRows { get; set; }
    public int TitleRows { get; set; }
}

internal static class OwnedListRepost
{
    public static OwnedListRepostResult Decode(string? json)
    {
        try { return JsonSerializer.Deserialize<OwnedListRepostResult>(json ?? "", new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true }) ?? new OwnedListRepostResult { Message = "응답 없음" }; }
        catch (JsonException) { return new OwnedListRepostResult { Message = "목록 결과 분석 실패" }; }
    }

    public static string BuildScript(string number, string title, bool? click,
        string minQuantity = "", string maxQuantity = "") =>
        BrowserScript.Replace("__ARG__", JsonSerializer.Serialize(new
            { number, title, click, minQuantity, maxQuantity }), StringComparison.Ordinal);

    // All controls are located from the current signed-in OWN listing page.
    // In particular, a page-level '재등록' button is NOT assumed to be the user's item.
    public const string BrowserScript = """
    (() => {
      const arg=__ARG__;
      const norm=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
      // My Room sometimes prepends a SPLIT-QUANTITY range to .trade_title,
      // including the unbracketed "1 ~ 9,995" form. It is not an item level.
      // Strip only a complete leading range, then still require the ENTIRE
      // actual product title to match. A matching ID alone is never enough.
      const coreTitle=s=>norm(String(s||'')
        .replace(/^\s*\[\s*[\d,]+\s*(?:개)?\s*[~〜～–-]\s*[\d,]+\s*(?:개)?\s*\]\s*/, '')
        .replace(/^\s*[\d,]+\s*(?:개)?\s*[~〜～–-]\s*[\d,]+\s*(?:개)?(?=\s|[^\d,])\s*/, ''));
      // Observed My Room issue: "10 레벨 작열..." is sometimes rendered
      // as "1 ~ 9,995 레벨 작열..." because a split-quantity label masks
      // the FIRST level number. Allow this narrow correction only if the
      // displayed range exactly equals the stored verified min/max quantity,
      // the saved item starts with a known gem level, and EVERY remaining
      // title character matches. An ID alone or an arbitrary suffix is NEVER
      // enough to authorize a repost.
      const maskedLeadingGemLevel=siteTitle=>{
        const label=String(siteTitle||'').trim();
        const range=label.match(/^\s*\[?\s*([\d,]+)\s*(?:개)?\s*[~〜～–-]\s*([\d,]+)\s*(?:개)?\s*\]?\s*/);
        if(!range)return false;
        const integer=s=>/^\d{1,7}$/.test(String(s||'').replace(/,/g,''))
          ?Number(String(s).replace(/,/g,'')):0;
        const min=integer(arg.minQuantity),max=integer(arg.maxQuantity);
        if(min<1||max<min||integer(range[1])!==min||integer(range[2])!==max)return false;
        const saved=coreTitle(arg.title).match(/^(8|9|10)(레벨.+)$/);
        return !!saved && norm(label.slice(range[0].length))===saved[2];
      };
      const valid=s=>/^\d{8,20}$/.test(String(s||''));
      const result=(status,message='',stamp='',diagnostic={})=>({status,message,stamp,...diagnostic});
      const official=()=>location.protocol==='https:' &&
        (location.hostname==='itemmania.com'||location.hostname.endsWith('.itemmania.com'));
      const ownList=()=>/^\/myroom\/(sell\/sell_regist|buy\/buy_regist)\.html$/i.test(location.pathname);
      if(!official()||!ownList())return result('error','현재 화면이 내 판매·구매 등록글 목록이 아닙니다');
      if(!valid(arg.number)||norm(arg.title).length<2)return result('error','등록번호 또는 제목이 유효하지 않습니다');
      const visible=e=>!!e&&e.isConnected&&e.getClientRects().length>0&&
        getComputedStyle(e).visibility!=='hidden' && !e.disabled && e.getAttribute('aria-disabled')!=='true';
      const docs=[document];const seen=new Set(docs);
      for(let i=0;i<docs.length&&i<8;i++)for(const frame of docs[i].querySelectorAll('iframe,frame')){
        try{const d=frame.contentDocument;if(d&&!seen.has(d)){seen.add(d);docs.push(d)}}catch{}
      }
      // Current authenticated My Room markup uses:
      // <a onclick="reInsert('listing-id','game-id')" class="list_icon reregist"
      //    data-desc="재등록"><span class="list_icon_img"></span></a>
      // Its icon is CSS-based (not SVG). Bind the exact onclick ID to THIS row.
      const className=e=>typeof e?.className==='string'?e.className:(e?.className?.baseVal||'');
      const names=e=>{
        const nested=typeof e.querySelectorAll==='function'
          ?[...e.querySelectorAll('[title],[aria-label],svg title')].slice(0,8):[];
        return [e.value,e.innerText,e.textContent,e.getAttribute?.('aria-label'),
          e.getAttribute?.('title'),e.getAttribute?.('data-desc'),
          e.getAttribute?.('data-tooltip'),e.getAttribute?.('data-original-title'),
          ...nested.flatMap(x=>[x.getAttribute?.('title'),x.getAttribute?.('aria-label'),x.textContent])]
          .map(norm).filter(Boolean);
      };
      const repostWords=/^(재등록|재등록하기|다시등록|다시등록하기)$/;
      const exactReinsert=e=>{
        const handler=String(e.getAttribute?.('onclick')||'');
        const match=handler.match(/^\s*reInsert\s*\(\s*(['"])(\d{8,20})\1\s*,/);
        return !!match && match[2]===arg.number &&
          /(?:^|\s)reregist(?:\s|$)/.test(className(e)) &&
          norm(e.getAttribute?.('data-desc'))==='재등록';
      };
      const iconName=e=>{
        const icons=typeof e.querySelectorAll==='function'
          ?[...e.querySelectorAll('svg,i,img')].slice(0,5):[];
        // An icon button must contain exactly one icon: a whole card/table
        // containing several icon actions cannot pass this test.
        if(icons.length!==1)return false;
        const signature=[e,...icons].map(x=>[
          className(x),x.getAttribute?.('data-icon'),x.getAttribute?.('data-testid'),
          x.getAttribute?.('aria-label'),x.getAttribute?.('title'),
          x.getAttribute?.('alt'),x.getAttribute?.('src')].filter(Boolean).join(' ')).join(' ').toLowerCase();
        if (/(?:^|[^a-z])(history|rotate-ccw|rotate-cw|refresh|reload|repost|re-register|reregister|clock-rotate-left|clock-arrow)(?:$|[^a-z])/.test(signature))
          return true;
        // Lucide 'history' SVG also has a distinctive clock hand and rewind
        // arrow. Use BOTH path fragments, never merely the presence of SVG.
        const paths=typeof icons[0].querySelectorAll==='function'
          ?[...icons[0].querySelectorAll('path')].map(x=>x.getAttribute?.('d')||'').join(' '):'';
        return /M12\s*7v5l4\s*2/.test(paths) && /M3\s*3v5h5/.test(paths);
      };
      const matching=[];const candidateRows=new Set();
      let numberRows=0,titleRows=0;
      const actualTitles=[];
      const attrs=['data-tid','data-listing-id','data-goods-id','data-trade-id','data-id'];
      for(const d of docs){
        for(const tr of d.querySelectorAll('tr'))candidateRows.add(tr);
        for(const el of d.querySelectorAll(attrs.map(a=>'['+a+']').join(','))){
          const row=el.closest('tr,li,article')||el;
          candidateRows.add(row);
        }
        for(const el of d.querySelectorAll('a[onclick],button[onclick],input[onclick]')){
          if(!String(el.getAttribute('onclick')||'').includes(arg.number))continue;
          const row=el.closest('tr,li,article,[data-tid],[data-listing-id]');
          if(row)candidateRows.add(row);
        }
      }
      for(const row of candidateRows){
        const text=String(row.innerText||row.textContent||'');
        if(text.length>6000)continue;
        const titleElement=row.querySelector?.('.trade_title');
        const itemTitle=String(titleElement?.textContent||'').trim();
        // The title can be shortened or decorated for display, while its
        // title/aria-label holds the full original text. Read only title-
        // scoped attributes, never a tooltip from a delete/repost action.
        const titleCandidates=[itemTitle,...['title','aria-label','data-title','data-original-title']
          .map(key=>titleElement?.getAttribute?.(key)||'')].filter(s=>String(s).trim().length>1);
        const titleMatches=norm(text).includes(norm(arg.title)) ||
          titleCandidates.some(siteTitle=>coreTitle(siteTitle)===coreTitle(arg.title)) ||
          (titleCandidates.length>0 && maskedLeadingGemLevel(itemTitle) &&
           // Never override a contradictory full original title attribute.
           !titleCandidates.slice(1).some(siteTitle=>coreTitle(siteTitle)!==coreTitle(arg.title)));
        if(titleMatches)titleRows++;
        const ids=new Set();
        for(const el of [row,...row.querySelectorAll('*')].slice(0,250)){
          for(const name of attrs){
            const value=el.getAttribute?.(name)||'';
            if(valid(value))ids.add(value);
          }
          for(const name of ['href','onclick','data-url','data-href']){
            const value=el.getAttribute?.(name)||'';
            if(!value.includes(arg.number))continue;
            // Require the exact number, not a prefix of another listing ID.
            if(new RegExp('(?:^|\\D)'+arg.number+'(?!\\d)').test(value))ids.add(arg.number);
          }
        }
        if(new RegExp('(?:^|\\D)'+arg.number+'(?!\\d)').test(text))ids.add(arg.number);
        if(ids.has(arg.number)){
          numberRows++;
          if(itemTitle&&actualTitles.length<2)actualTitles.push(itemTitle.slice(0,120));
        }
        if(!ids.has(arg.number)||!titleMatches)continue;
        if(ids.size!==1)return result('error','한 물품 행에 서로 다른 등록번호가 있어 중단했습니다','',
          {scannedRows:candidateRows.size,numberRows,titleRows});
        const controls=[...new Set(row.querySelectorAll(
          'button,input[type="button"],input[type="submit"],a[href],a[onclick],[role="button"],[onclick],[class*="cursor-pointer"]'))]
          .filter(visible);
        const actions=controls.filter(e=>{
          const handler=String(e.getAttribute?.('onclick')||'');
          // The current site's inline handler is authoritative: never allow
          // a hide/delete/other onclick to be rescued by icon/text guesses.
          if (handler.trim()) return exactReinsert(e);
          return exactReinsert(e) || names(e).some(v=>repostWords.test(v)) || iconName(e);
        });
        const stampNodes=[...row.querySelectorAll('td.flex_box p.f_gray')];
        const stampText=stampNodes.length===1?String(stampNodes[0].textContent||'').trim():'';
        const stamp=/^\d{2}-\d{2}\s+\d{2}:\d{2}$/.test(stampText)?stampText.replace(/\s+/g,' '):'';
        matching.push({row,actions,stamp});
      }
      const diagnostic={scannedRows:candidateRows.size,numberRows,titleRows};
      if(matching.length===0&&numberRows>0)return result('mismatch',
        actualTitles.length
          ?'등록번호는 있지만 저장된 제목과 사이트 제목이 다릅니다. 저장 제목: '+
            String(arg.title).slice(0,100)+' / 사이트 제목: '+actualTitles.join(' / ')
          :'등록번호는 있지만 사이트의 물품 제목 영역을 확인하지 못했습니다. 원본 글을 다시 확인하세요',
        '',diagnostic);
      if(matching.length!==1)return result(matching.length?'error':'not-found',matching.length?
        '동일 등록번호와 제목의 행이 여러 개라 중단했습니다':
        '이 페이지에서 등록번호와 제목이 일치하는 행을 찾지 못했습니다 (다른 페이지일 수 있음)','',diagnostic);
      if(arg.click===null)return result('found','현재 내 등록글 목록에서 해당 등록번호·제목을 다시 확인함',matching[0].stamp,diagnostic);
      if(matching[0].actions.length!==1)return result('error',matching[0].actions.length?
        '해당 행에 재등록 버튼이 여러 개입니다':'해당 물품 행에서 등록번호와 연결된 reInsert 재등록 버튼을 확인하지 못했습니다','',diagnostic);
      if(!arg.click)return result('ready','해당 등록번호·제목의 재등록 버튼 1개 확인',matching[0].stamp,diagnostic);
      const button=matching[0].actions[0];
      // Prefer the native site handler with the two arguments read from the
      // EXACT matched row. This avoids the DOM click path used in older builds.
      // If the function is not globally available, keep the verified-row click
      // as a fallback. Never retry the click after an invoked handler throws.
      const handler=String(button.getAttribute?.('onclick')||'');
      const call=handler.match(/^\s*reInsert\s*\(\s*(['"])(\d{8,20})\1\s*,\s*(['"])([a-zA-Z0-9_-]{1,40})\3\s*\)\s*;?\s*$/);
      if(call && call[2]===arg.number && typeof window!=='undefined' &&
         typeof window.reInsert==='function') {
        try {
          window.reInsert(call[2],call[4]);
          return {...result('clicked','사이트 자체 재등록 함수 호출 (성공 여부는 사이트에서 다시 검증)',matching[0].stamp),method:'native'};
        } catch(e) {
          return result('error','사이트 자체 재등록 함수 처리 오류; 중복 제출하지 않습니다');
        }
      }
      button.click();
      return {...result('clicked','검증된 물품 행 재등록 버튼 클릭 (성공 여부는 별도 검증)',matching[0].stamp),method:'row-click'};
    })()
    """;
}
