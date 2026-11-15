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
}

internal static class OwnedListRepost
{
    public static OwnedListRepostResult Decode(string? json)
    {
        try { return JsonSerializer.Deserialize<OwnedListRepostResult>(json ?? "", new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true }) ?? new OwnedListRepostResult { Message = "응답 없음" }; }
        catch (JsonException) { return new OwnedListRepostResult { Message = "목록 결과 분석 실패" }; }
    }

    public static string BuildScript(string number, string title, bool? click) =>
        BrowserScript.Replace("__ARG__", JsonSerializer.Serialize(new { number, title, click }), StringComparison.Ordinal);

    // All controls are located from the current signed-in OWN listing page.
    // In particular, a page-level '재등록' button is NOT assumed to be the user's item.
    public const string BrowserScript = """
    (() => {
      const arg=__ARG__;
      const norm=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
      const valid=s=>/^\d{8,20}$/.test(String(s||''));
      const result=(status,message='')=>({status,message});
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
      const matching=[];const candidateRows=new Set();
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
        if(text.length>6000||!norm(text).includes(norm(arg.title)))continue;
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
        if(!ids.has(arg.number))continue;
        if(ids.size!==1)return result('error','한 물품 행에 서로 다른 등록번호가 있어 중단했습니다');
        const actions=[...row.querySelectorAll('button,input[type="button"],input[type="submit"],a[href],a[onclick],[role="button"]')]
          .filter(visible).filter(e=>/^(재등록|재등록하기|다시등록|다시등록하기)$/.test(norm(e.value||e.innerText||e.textContent||e.getAttribute('aria-label')||e.title)));
        matching.push({row,actions});
      }
      if(matching.length!==1)return result(matching.length?'error':'not-found',matching.length?
        '동일 등록번호와 제목의 행이 여러 개라 중단했습니다':
        '이 페이지에서 등록번호와 제목이 일치하는 행을 찾지 못했습니다 (다른 페이지일 수 있음)');
      if(arg.click===null)return result('found','현재 내 등록글 목록에서 해당 등록번호·제목을 다시 확인함');
      if(matching[0].actions.length!==1)return result('error',matching[0].actions.length?
        '해당 행에 재등록 버튼이 여러 개입니다':'해당 물품 행에 독립적인 재등록 버튼이 없습니다');
      if(!arg.click)return result('ready','해당 등록번호·제목의 재등록 버튼 1개 확인');
      const button=matching[0].actions[0];
      button.click();
      return result('clicked','해당 물품 행의 재등록 버튼 클릭됨 (최종 등록 성공은 별도 확인 필요)');
    })()
    """;
}
