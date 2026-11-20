using System;

namespace LuzenBrowserReplica;

// One read-only identity parser shared by the repost and price-edit paths.
// The site's title, split-quantity label, server link, and trade-side are
// inspected independently. Never authorize an action from a matching ID alone.
internal static class OwnedListingTitleIdentity
{
    public static string Inject(string script) =>
        script.Replace("__OWNED_TITLE_IDENTITY__", BrowserScript, StringComparison.Ordinal);

    public const string BrowserScript = """
      const ownedTitleIdentity=(row,expected)=>{
        const compact=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
        const titleNode=row.querySelector?.('.trade_title');
        if(!titleNode)return {ok:false,reason:'물품 제목 영역 없음',title:''};
        const raw=String(titleNode.textContent||'').trim();
        const original=['title','aria-label','data-title','data-original-title']
          .map(attr=>String(titleNode.getAttribute?.(attr)||'').trim()).filter(Boolean);
        const split=String(row.querySelector?.('.sub_trade_title')?.textContent||'').trim();
        const rangeRx=/^\s*\[?\s*([\d,]+)\s*(?:개)?\s*[~〜～–-]\s*([\d,]+)\s*(?:개)?\s*\]?\s*/;
        const integer=s=>{const raw=String(s||'').replace(/,/g,'');return /^\d{1,7}$/.test(raw)?Number(raw):0};
        const parseRange=s=>{
          const match=String(s||'').match(rangeRx);
          if(!match)return null;
          const min=integer(match[1]),max=integer(match[2]);
          return min>=1&&max>=min?{min,max,prefix:match[0]}:null;
        };
        const shownRange=parseRange(raw),splitRange=parseRange(split);
        // Two independent quantity fields must agree; a conflicting label
        // is not evidence that the title was merely reformatted.
        if(shownRange&&splitRange&&(shownRange.min!==splitRange.min||shownRange.max!==splitRange.max))
          return {ok:false,reason:'사이트 분할수량 표시가 서로 다름',title:raw};
        const siteRange=splitRange||shownRange;
        const min=integer(expected.minQuantity),max=integer(expected.maxQuantity);
        if(siteRange&&min&&max&&(min!==siteRange.min||max!==siteRange.max))
          return {ok:false,reason:'사이트 분할수량과 저장 수량 불일치',title:raw};
        // Category/server links are independent from the title and ID.
        const servers=new Set();
        const attrServer=row.getAttribute?.('data-server')||row.getAttribute?.('data-server-name');
        if(attrServer)servers.add(compact(attrServer));
        for(const a of row.querySelectorAll?.('a[href]')||[]){
          const href=String(a.getAttribute?.('href')||'');
          if(!/\/list_search\.html/i.test(href))continue;
          const match=href.match(/[?&]search_server_text=([^&#]+)/i);
          if(match){try{servers.add(compact(decodeURIComponent(match[1].replace(/\+/g,' '))))}catch{}}
        }
        const wantedServer=compact(expected.server);
        if(wantedServer&&servers.size&&(servers.size!==1||!servers.has(wantedServer)))
          return {ok:false,reason:'사이트 서버와 저장 서버 불일치',title:raw};
        const side=/\/buy\//i.test(location.pathname)?'구매':'판매';
        if(expected.tradeSide&&expected.tradeSide!==side)
          return {ok:false,reason:'사이트 거래 방향과 저장 정보 불일치',title:raw};
        const core=s=>compact(String(s||'').replace(rangeRx,''));
        const saved=core(expected.title);
        const candidates=[raw,...original];
        // Title-scoped original attributes outrank the visual text. A
        // conflicting full original title must never be silently ignored.
        if(original.some(value=>core(value)!==saved))
          return {ok:false,reason:'원본 제목 속성이 저장 제목과 불일치',title:raw};
        const exact=candidates.some(value=>core(value)===saved);
        if(exact)return {ok:true,reason:'원본 제목 대조',title:raw,quantityVerified:!!siteRange&&!!min&&!!max};
        // The site can replace ONLY the first 8/9/10-level token with
        // a verified split-range label. Compare the entire remaining text,
        // including any additional level tokens and punctuation.
        if(shownRange&&siteRange&&min&&max&&siteRange.min===min&&siteRange.max===max){
          const savedTokens=saved.match(/^(8|9|10)((?:8|9|10)*레벨.+)$/);
          if(savedTokens&&compact(raw.slice(shownRange.prefix.length))===savedTokens[2])
            return {ok:true,reason:'분할수량에 가린 첫 레벨 복원',title:raw,quantityVerified:true};
        }
        return {ok:false,reason:'물품 원본 제목 불일치',title:raw};
      };
    """;
}
