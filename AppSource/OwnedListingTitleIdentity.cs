using System;

namespace LuzenBrowserReplica;

// Shared read-only evidence parser for repost and site-price editing.
// A listing number must be independently bound to the exact OWN-list row;
// server, quantity and trade side veto contradictory evidence. Displayed
// titles are advisory only because the site reformats/masks them frequently.
internal static class OwnedListingTitleIdentity
{
    public static string Inject(string script) =>
        script.Replace("__OWNED_TITLE_IDENTITY__", BrowserScript, StringComparison.Ordinal);

    public const string BrowserScript = """
      const ownedTitleIdentity=(row,expected)=>{
        const compact=s=>String(s||'').replace(/\s+/g,'').toLowerCase();
        const titleNode=row.querySelector?.('.trade_title');
        const raw=String(titleNode?.textContent||'').trim();
        const original=['title','aria-label','data-title','data-original-title']
          .map(attr=>String(titleNode?.getAttribute?.(attr)||'').trim()).filter(Boolean);
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
        // Only a dedicated split-quantity field is independent quantity
        // evidence. Digits embedded in the displayed TITLE can be decoration
        // or a site masking artifact and must never veto an ID-bound row.
        const siteRange=splitRange;
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
        // A contradictory display/tooltip title is diagnostic information,
        // never a veto of a strongly ID-bound OWN-list item. No title value
        // is used as permission to submit or infer the listing number.
        const exact=!!saved&&candidates.some(value=>core(value)===saved);
        let titleMatches=exact&&!original.some(value=>core(value)!==saved);
        // The site can replace ONLY the first 8/9/10-level token with
        // a verified split-range label. Compare the entire remaining text,
        // including any additional level tokens and punctuation.
        if(!titleMatches&&shownRange&&siteRange&&min&&max&&siteRange.min===min&&siteRange.max===max&&
           !original.some(value=>core(value)!==saved)){
          const savedTokens=saved.match(/^(8|9|10)((?:8|9|10)*레벨.+)$/);
          if(savedTokens&&compact(raw.slice(shownRange.prefix.length))===savedTokens[2])
            titleMatches=true;
        }
        return {ok:true,titleMatches,
          reason:titleMatches?'제목 참고 일치':'사이트 제목 형식 차이 (제출 차단 아님)',
          title:raw,quantityVerified:!!siteRange&&!!min&&!!max};
      };
    """;
}
