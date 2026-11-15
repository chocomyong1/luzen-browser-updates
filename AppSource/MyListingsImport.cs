using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

// Read-only import of listings ALREADY posted by the signed-in ItemMania account.
// These are records, not newly posted items. Import never submits, clicks a
// trade button, or enables unattended reposting.
internal sealed class ExistingListingRecord
{
    public string ListingNumber { get; set; } = "";
    public string ListingUrl { get; set; } = "";
    public string SourceListUrl { get; set; } = "";
    public string TradeSide { get; set; } = ""; // 판매 / 구매 / 알 수 없음
    public string Game { get; set; } = "";
    public string Server { get; set; } = "";
    public string Title { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Price { get; set; } = "";
    public string Status { get; set; } = "";
}

internal sealed class ExistingListingPage
{
    public string Error { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public bool LoginRequired { get; set; }
    public bool IsEmptyList { get; set; }
    public List<ExistingListingRecord> Listings { get; set; } = new();
    public List<string> NextPageUrls { get; set; } = new();
}

internal sealed class ExistingListingMergeSummary
{
    public List<ListingJob> NewJobs { get; } = new();
    public int Duplicates { get; set; }
    public int Invalid { get; set; }
    public int WithoutUrl { get; set; }
    public List<ExistingListingUpdate> Updates { get; } = new();
}

// Never overwrite a user-edited value with an uncertain scraped value.
// Re-import fills ONLY empty fields of posts already imported from the site.
internal sealed class ExistingListingUpdate
{
    public ListingJob Target { get; set; } = null!;
    public string Game { get; set; } = "";
    public string Server { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Price { get; set; } = "";
    public string EntryUrl { get; set; } = "";
    public string SourceListUrl { get; set; } = "";
    public bool HasChanges => new[] { Game, Server, Quantity, Price, EntryUrl, SourceListUrl }.Any(v => !string.IsNullOrEmpty(v));
    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(Target.Game)) Target.Game = Game;
        if (string.IsNullOrWhiteSpace(Target.Server)) Target.Server = Server;
        if (string.IsNullOrWhiteSpace(Target.Quantity)) Target.Quantity = Quantity;
        if (string.IsNullOrWhiteSpace(Target.Price)) Target.Price = Price;
        if (string.IsNullOrWhiteSpace(Target.EntryUrl)) Target.EntryUrl = EntryUrl;
        if (string.IsNullOrWhiteSpace(Target.SourceListUrl)) Target.SourceListUrl = SourceListUrl;
        Target.RequiresReview = true;
        Target.Enabled = false;
        Target.NextDueUtc = DateTime.MaxValue;
        Target.LastStatus = "기존 글 정보 보완됨: 검토 후 재등록";
    }
}

internal static class MyListingsImport
{
    // Verified on the ItemMania homepage's official "마이룸" navigation (2026-11-14).
    // Do not guess account-private endpoints or ask the user to paste URLs.
    public const string MyRoomHome = "https://www.itemmania.com/myroom/";
    public const string SellListingsUrl = "https://www.itemmania.com/myroom/sell/sell_regist.html";
    public const string BuyListingsUrl = "https://www.itemmania.com/myroom/buy/buy_regist.html";

    public static bool IsSellListUrl(string? url) => IsOwnedListUrl(url) &&
        new Uri(url!).AbsolutePath.Equals("/myroom/sell/sell_regist.html", StringComparison.OrdinalIgnoreCase);

    public static bool IsOwnedListUrl(string? url)
    {
        if (!IsOfficialUrl(url)) return false;
        var path = new Uri(url!).AbsolutePath;
        return path.Equals("/myroom/sell/sell_regist.html", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/myroom/buy/buy_regist.html", StringComparison.OrdinalIgnoreCase);
    }


    // Exact official domain match; never trust `itemmania.com.evil.invalid`.
    public static bool IsOfficialUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.Host.Equals("itemmania.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".itemmania.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string CanonicalUrl(string? url)
    {
        if (!IsOfficialUrl(url)) return "";
        var parsed = new Uri(url!);
        // Fragments do not distinguish listings. Keep query parameters, which may
        // contain listing IDs. Never rewrite a listing URL into an invented URL.
        return parsed.GetLeftPart(UriPartial.Path).TrimEnd('/') + parsed.Query;
    }

    private static string Clean(string? text, int limit = 350)
    {
        var value = (text ?? "").Replace('\0', ' ').Trim();
        return value.Length > limit ? value[..limit] : value;
    }

    public static ExistingListingMergeSummary Prepare(IEnumerable<ExistingListingRecord> records, IEnumerable<ListingJob> existing,
        string subId, string siteId)
    {
        var result = new ExistingListingMergeSummary();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingRecords = new Dictionary<string, ListingJob>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in existing)
        {
            var id = (job.ListingNumber ?? "").Trim();
            if (Regex.IsMatch(id, @"^\d{8,20}$"))
            {
                var key = "N:" + siteIdKey(job.SiteId) + ":" + id;
                identities.Add(key);
                existingRecords.TryAdd(key, job);
            }
            var url = CanonicalUrl(job.EntryUrl);
            if (!string.IsNullOrWhiteSpace(url))
            {
                var key = "U:" + siteIdKey(job.SiteId) + ":" + url;
                identities.Add(key);
                existingRecords.TryAdd(key, job);
            }
        }
        foreach (var row in records)
        {
            if (row is null) { result.Invalid++; continue; }
            var number = Clean(row.ListingNumber, 24);
            var url = CanonicalUrl(row.ListingUrl);
            var title = Clean(row.Title);
            if (string.IsNullOrWhiteSpace(title) || (string.IsNullOrWhiteSpace(url) && !Regex.IsMatch(number, @"^\d{8,20}$")))
            { result.Invalid++; continue; }
            string numberKey = Regex.IsMatch(number, @"^\d{8,20}$") ? "N:" + siteIdKey(siteId) + ":" + number : "";
            string urlKey = !string.IsNullOrEmpty(url) ? "U:" + siteIdKey(siteId) + ":" + url : "";
            if ((!string.IsNullOrEmpty(numberKey) && identities.Contains(numberKey)) ||
                (!string.IsNullOrEmpty(urlKey) && identities.Contains(urlKey)))
            {
                // A previous import may have contained a title/ID but no price,
                // quantity, game or server. Fill those blanks on re-import.
                ListingJob? target = null;
                if (!string.IsNullOrEmpty(numberKey)) existingRecords.TryGetValue(numberKey, out target);
                if (target is null && !string.IsNullOrEmpty(urlKey)) existingRecords.TryGetValue(urlKey, out target);
                if (target is not null && target.ImportedFromSite &&
                    (string.IsNullOrEmpty(target.OriginalTradeSide) ||
                     target.OriginalTradeSide == row.TradeSide))
                {
                    var proposedGame = Clean(row.Game, 80);
                    var proposedServer = GameServerCatalog.CanonicalServer(proposedGame, Clean(row.Server, 80)) ?? Clean(row.Server, 80);
                    var update = new ExistingListingUpdate
                    {
                        Target = target,
                        Game = string.IsNullOrWhiteSpace(target.Game) ? proposedGame : "",
                        Server = string.IsNullOrWhiteSpace(target.Server) ? proposedServer : "",
                        Quantity = string.IsNullOrWhiteSpace(target.Quantity) ? Clean(row.Quantity, 30) : "",
                        Price = string.IsNullOrWhiteSpace(target.Price) ? Clean(row.Price, 30) : "",
                        EntryUrl = string.IsNullOrWhiteSpace(target.EntryUrl) ? url : "",
                        SourceListUrl = string.IsNullOrWhiteSpace(target.SourceListUrl) &&
                            IsOwnedListUrl(row.SourceListUrl) ? CanonicalUrl(row.SourceListUrl) : ""
                    };
                    if (update.HasChanges) result.Updates.Add(update);
                    else result.Duplicates++;
                }
                else result.Duplicates++;
                continue;
            }
            if (!string.IsNullOrEmpty(numberKey)) identities.Add(numberKey);
            if (!string.IsNullOrEmpty(urlKey)) identities.Add(urlKey);

            var game = Clean(row.Game, 80);
            var server = GameServerCatalog.CanonicalServer(game, Clean(row.Server, 80)) ?? Clean(row.Server, 80);
            var side = Clean(row.TradeSide, 30);
            if (side is not ("판매" or "구매")) side = "알 수 없음";
            if (string.IsNullOrEmpty(url)) result.WithoutUrl++;
            var job = new ListingJob
            {
                Operation = "재등록", OriginalTradeSide = side,
                ListingNumber = Regex.IsMatch(number, @"^\d{8,20}$") ? number : "",
                ImportedFromSite = true, ImportedUtc = DateTime.UtcNow,
                SiteId = siteId, SubId = subId,
                Game = game, Server = server, Title = title,
                // Never infer a site's category/trading mode from a list-row screenshot.
                // Reposting uses the original listing URL, not a new listing form.
                ItemCategory = "", TradingMethod = "",
                Quantity = Clean(row.Quantity, 30), Price = Clean(row.Price, 30), EntryUrl = url,
                SourceListUrl = IsOwnedListUrl(row.SourceListUrl) ? CanonicalUrl(row.SourceListUrl) : "",
                Enabled = false, AutoRepost = true, InitialCompleted = true,
                RequiresReview = true, NextDueUtc = DateTime.MaxValue,
                LastStatus = string.IsNullOrEmpty(url) ? "가져옴: 관리 목록에서 재등록 확인 필요" : "가져옴: 재등록 전 확인 필요"
            };
            result.NewJobs.Add(job);
        }
        return result;
    }

    private static string siteIdKey(string? siteId) => (siteId ?? "").Trim().ToLowerInvariant();

    public static ExistingListingPage DecodePage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ExistingListingPage { Error = "빈 응답" };
        try
        {
            return JsonSerializer.Deserialize<ExistingListingPage>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                MaxDepth = 15
            }) ?? new ExistingListingPage { Error = "파싱할 수 없는 응답" };
        }
        catch (JsonException) { return new ExistingListingPage { Error = "사이트의 목록 응답을 해석할 수 없습니다." }; }
    }

    // This read-only DOM extractor only accepts one of the two verified OWN
    // registration-list paths, not the public marketplace, checkout, etc.
    // Handles ordinary tables, some div-based lists, and same-origin frames.
    // Site HTML behind a signed-in session is unavailable in this environment;
    // uncertainty remains visible as an error instead of a fake success.
    public const string ExtractPageScript = """
(() => {
  const norm = s => String(s || '').replace(/\s+/g,' ').trim();
  const clip = (s,n=350) => norm(s).slice(0,n);
  const clean = s => norm(s).replace(/[\s:*：()]/g,'');
  const official = u => { try { const x=new URL(u,location.href); return x.protocol==='https:' &&
    (x.hostname==='itemmania.com' || x.hostname.endsWith('.itemmania.com')); } catch { return false; } };
  const ownPath = p => /^\/myroom\/(sell\/sell_regist|buy\/buy_regist)\.html$/i.test(p);
  const pageUrl=location.href;
  const fail=(error,loginRequired=false,isEmptyList=false) =>
    ({error,pageUrl,loginRequired,isEmptyList,listings:[],nextPageUrls:[]});
  if (!official(pageUrl) || !ownPath(location.pathname))
    return fail('내 판매·구매 등록물품 관리 페이지가 아닙니다. 로그인 상태를 확인하세요.',
      /login|member|auth|signin/i.test(location.pathname));
  const side=/\/buy\//i.test(location.pathname) ? '구매':'판매';
  // MyRoom has several table/card layouts. Headers are often absent even
  // though the values appear in data-* attributes or labelled descendants.
  // Only read explicit field labels/attributes; do NOT guess a price or qty.
  const fieldLabels={game:['게임','게임명'],server:['서버','서버명'],
    quantity:['수량','판매수량','구매수량','분할수량','보유수량'],
    price:['가격','금액','구매금액','판매금액','분할금액','단가']};
  const dataNames={game:['data-game','data-game-name'],
    server:['data-server','data-server-name'],
    quantity:['data-quantity','data-qty','data-count'],
    price:['data-price','data-amount','data-money']};
  const safeValue=(value,type)=>{
    const v=clip(value,100); if(!v) return '';
    if(type==='price') {
      const m=v.match(/^\s*(?:₩\s*)?([0-9][0-9,]*)\s*(?:원|KRW)?\s*$/i);
      return m ? m[1] : '';
    }
    if(type==='quantity') {
      const m=v.match(/^\s*([0-9][0-9,]*)\s*(?:개|건|만|억|조)?\s*$/);
      return m ? m[1] : '';
    }
    return v.length<=80 ? v : '';
  };
  const fieldFromRow=(row,type)=>{
    const matches=[];
    for(const el of [row,...row.querySelectorAll('*')].slice(0,180)) {
      for(const a of dataNames[type]) {
        const v=el.getAttribute?.(a);
        if(v) matches.push(safeValue(v,type));
      }
      // Table cells in modern layout often carry data-label="판매금액".
      for(const labelAttr of ['data-label','aria-label']) {
        const label=clean(el.getAttribute?.(labelAttr)||'');
        if(label && fieldLabels[type].some(v=>label===v))
          matches.push(safeValue(el.innerText||el.textContent,type));
      }
      if(el.children.length<=1 && /^(DT|TH|LABEL|STRONG|B|SPAN)$/i.test(el.tagName)) {
        const label=clean(el.textContent);
        if(fieldLabels[type].some(v=>label===v)) {
          const sibling=el.nextElementSibling;
          if(sibling) matches.push(safeValue(sibling.innerText||sibling.textContent,type));
        }
      }
    }
    const classHints={game:/\bgame(?:-?name)?\b/i,server:/\bserver(?:-?name)?\b/i,
      quantity:/\b(?:quantity|qty|count)\b/i,price:/\b(?:price|amount|money|cost)\b/i};
    for(const el of row.querySelectorAll('[class],[id]')) {
      if(!classHints[type].test(String(el.className||'')+' '+String(el.id||''))) continue;
      if(el.children.length>2)continue;
      matches.push(safeValue(el.innerText||el.textContent,type));
    }
    const texts=[...new Set(matches.filter(Boolean))];
    return texts.length===1 ? texts[0] : '';
  };
  const fieldFromText=(row,type)=>{
    const body=clip(row.innerText||row.textContent,1600);
    const labels=fieldLabels[type].join('|');
    // Only explicit "게임: 로스트아크" / "수량: 1개"-like text.
    const re=new RegExp('(?:^|\\s)(?:'+labels+')\\s*[:：]\\s*([^|\\s]{1,60}(?:\\s*원)?)','gi');
    const values=[...body.matchAll(re)].map(m=>safeValue(m[1],type)).filter(Boolean);
    return [...new Set(values)].length===1 ? values[0] : '';
  };
  const lostArkServers=['루페온','실리안','아만','카마인','카제로스','아브렐슈드','카단','니나브'];
  const categoryPair=raw=>{
    const t=clip(raw,700);
    if(!t.includes('로스트아크'))return {game:'',server:''};
    const found=lostArkServers.filter(s=>t.includes(s));
    return {game:'로스트아크',server:found.length===1?found[0]:''};
  };
  const docs=[document], seenDocs=new Set([document]);
  for (let i=0;i<docs.length && i<8;i++) {
    for (const f of docs[i].querySelectorAll('iframe,frame')) {
      try { if (f.contentDocument && !seenDocs.has(f.contentDocument)) {
        seenDocs.add(f.contentDocument); docs.push(f.contentDocument);
      } } catch { /* Cross-origin frames are intentionally ignored. */ }
    }
  }
  const loginRequired=docs.some(d=>{
    let path=''; try {path=d.location.pathname||'';}catch{}
    const pw=d.querySelector('input[type="password"]');
    const hasSubmit=[...d.querySelectorAll('button,input[type="submit"]')].some(e=>/로그인|log\s*in|sign\s*in/i.test(clip(e.textContent||e.value,80)));
    return /login|signin|auth/i.test(path) ||
      (!!pw && (hasSubmit || /로그인|login/i.test(clip(d.title,180))));
  });
  if (loginRequired) return fail('로그인이 필요합니다. 매니아 계정 설정을 확인하세요.',true);

  const listingId=(value) => {
    const s=String(value||'');
    const a=s.match(/(?:^|[^0-9])([0-9]{12,20})(?![0-9])/);
    return a ? a[1] : '';
  };
  const getId=row=>{
    const els=[row,...row.querySelectorAll('[data-tid],[data-listing-id],[data-goods-id],[data-trade-id],[data-id]')].slice(0,20);
    for(const el of els) for(const name of ['data-tid','data-listing-id','data-goods-id','data-trade-id','data-id']) {
      const id=listingId(el.getAttribute(name)); if(id) return id;
    }
    for(const el of row.querySelectorAll('a[href],a[onclick],button[onclick],input[name]')) {
      for(const attr of ['href','onclick','data-tid','value']) {
        const v=el.getAttribute(attr)||'';
        if (/(goods|trade|item|article|tid|no|id|물품|등록)/i.test(v)) {
          const id=listingId(v); if(id) return id;
        }
      }
    }
    return listingId(row.innerText||row.textContent);
  };
  const candidateUrl=(row,id) => {
    // Some own-list pages use onclick/data-url rather than href for details.
    const direct=[];
    for(const el of row.querySelectorAll('a,button,[data-url],[data-href]')) {
      for(const attr of ['href','data-url','data-href','onclick']) {
        const src=el.getAttribute(attr)||'';
        const options=attr==='onclick'
          ? [...src.matchAll(/['"](https?:\/\/[^'"<>]+|\/[^'"<>]+)['"]/g)].map(m=>m[1])
          : [src];
        for(const link of options) {
          try {direct.push({href:new URL(link,pageUrl),text:clip(el.textContent)});}catch{}
        }
      }
    }
    const links=direct.filter(Boolean)
      .filter(a=>official(a.href.href) && !ownPath(a.href.pathname) &&
        /(goods|trade|item|detail|view|article|sell|buy)/i.test(a.href.pathname+a.href.search));
    const matching=links.filter(a=>id && a.href.href.includes(id));
    const pool=matching.length ? matching : links;
    const unique=[...new Set(pool.map(a=>a.href.href.split('#')[0]))];
    return unique.length===1 ? unique[0] : '';
  };
  const listings=[], signatures=new Set();
  const append=(row, title, header={}, values=[]) => {
    const id=getId(row);
    const url=candidateUrl(row,id);
    title=clip(title,350);
    if(!title || title.length < 2 || (!id && !url)) return;
    const key=id||url;
    if(signatures.has(key))return;
    signatures.add(key);
    const get=(name) => {
      const idx=header[name]??-1;
      return idx>=0 && idx<values.length ? clip(values[idx],100) : '';
    };
    const combined=categoryPair(clip(row.innerText||row.textContent,900));
    const sharedColumn=header.game>=0 && header.game===header.server;
    const pick=(type)=> {
      const column=safeValue(get(type),type);
      if ((type==='game'||type==='server') && sharedColumn)
        return type==='game'?combined.game:combined.server;
      return column || fieldFromRow(row,type) || fieldFromText(row,type) ||
        ((type==='game'||type==='server') ? combined[type] : '');
    };
    listings.push({listingNumber:id,listingUrl:url,sourceListUrl:pageUrl,tradeSide:side,
      game:pick('game'),server:pick('server'),title,quantity:pick('quantity'),
      price:pick('price'),status:get('status')});
  };
  for(const d of docs) {
    const tables=[...d.querySelectorAll('table')].slice(0,45);
    for(const t of tables) {
      const trs=[...t.querySelectorAll('tr')]; if(trs.length===0)continue;
      let heading=trs.find(r => r.querySelector('th')) || trs[0];
      const head=[...heading.children].filter(c=>/^(TH|TD)$/i.test(c.tagName)).map(c=>clean(c.textContent));
      const col=keys=>head.findIndex(x=>keys.some(k=>x.includes(k)));
      const indices={
        title:col(['물품제목','상품제목','물품명','상품명','제목','물품정보','상품정보']),
        game:col(['게임명','게임']),server:col(['서버명','서버']),
        quantity:col(['분할수량','수량']),price:col(['분할금액','판매금액','구매금액','가격','금액']),
        status:col(['등록상태','진행상태','상태'])
      };
      const candidates=trs.filter(r=>r!==heading && r.querySelector('td'));
      for(const row of candidates) {
        const cells=[...row.children].filter(c=>/^(TH|TD)$/i.test(c.tagName));
        if(!cells.length)continue;
        const values=cells.map(x=>x.innerText||x.textContent);
        const titleCell=cells[indices.title]||null;
        // Legacy item tables often omit a '제목' header, but still link the
        // listing title in an anchor. Prefer that over arbitrary row text.
        const anchors=[...(titleCell||row).querySelectorAll('a')]
          .map(a=>clip(a.innerText||a.textContent))
          .filter(x=>x.length>=3 && !/^(수정|삭제|재등록|상세|거래|취소|확인|보기)$/.test(x));
        let title=titleCell ? clip(titleCell.innerText||titleCell.textContent) : '';
        if(!title || title.length<3)title=anchors.sort((a,b)=>b.length-a.length)[0]||'';
        if(!title && getId(row)) {
          // If a list omits a title link, find the longest readable field.
          title=values.map(x=>clip(x)).filter(x=>x.length>=5 && x.length<250)
            .sort((a,b)=>b.length-a.length)[0]||'';
        }
        append(row,title,indices,values);
      }
    }
    // Some current UIs use list cards rather than <table>. Require an item ID
    // and a listing-related wrapper, not all elements on the page.
    for(const row of d.querySelectorAll('[data-tid],[data-listing-id],[data-goods-id],[data-trade-id]')) {
      if(row.closest('table'))continue;
      if(!getId(row))continue;
      const titleNode=row.querySelector('a[href],.title,.subject,[class*="item_name"],[class*="goods_name"]');
      if(!titleNode)continue;
      append(row,titleNode.innerText||titleNode.textContent);
    }
  }
  const nextPageUrls=[];
  for(const d of docs)for(const a of d.querySelectorAll('a[href]')) {
    let u; try {u=new URL(a.getAttribute('href'),pageUrl);}catch{continue;}
    if(!official(u.href)||!ownPath(u.pathname)||u.href===pageUrl)continue;
    const txt=clean(a.innerText||a.textContent), cls=(a.className||'').toString();
    const paging=!!a.closest('[class*="paging"],[class*="pagination"],[id*="paging"],[id*="pagination"],.paginate') ||
      /page|pag|pg=/i.test(u.search) || /paging|page/i.test(cls);
    if(paging && (/^(다음|next|>|»|[0-9]{1,3})$/i.test(txt) || /next/i.test(cls)) &&
       !nextPageUrls.includes(u.href)) nextPageUrls.push(u.href);
  }
  const isEmptyList=docs.some(d=>/등록(된|한)?\s*(판매|구매|물품|상품)?.{0,8}(없습니다|없어요)|등록\s*내역이\s*없|검색\s*결과가\s*없/.test(clip(d.body?.innerText||'',7000)));
  return {pageUrl,loginRequired:false,isEmptyList,
    error:listings.length ? '' : (isEmptyList?'등록된 물품이 없습니다.':'목록 형식이 확인되지 않았습니다. 입력값 또는 URL을 요구하지 않습니다.'),
    listings,nextPageUrls:nextPageUrls.slice(0,30)};
})()
""";

    // Only detail links that are confirmed to point to THIS listing can be
    // opened to fill missing values. Never invent a URL from a registration ID.
    public static bool IsSafeDetailUrl(string? url, string? number)
    {
        if (!IsOfficialUrl(url) || IsOwnedListUrl(url)) return false;
        if (!Regex.IsMatch(number ?? "", @"^\d{8,20}$")) return false;
        var uri = new Uri(url!);
        if (Regex.IsMatch(uri.AbsolutePath, @"/(login|auth|payment|charge|deposit|checkout)/", RegexOptions.IgnoreCase)) return false;
        return (uri.AbsolutePath + uri.Query).Contains(number!, StringComparison.Ordinal);
    }

    public static ExistingListingRecord DecodeDetail(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<ExistingListingRecord>(json ?? "", new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true }) ?? new ExistingListingRecord();
        }
        catch (JsonException) { return new ExistingListingRecord(); }
    }

    public static string BuildDetailScript(string expectedNumber) =>
        ExtractDetailScript.Replace("__EXPECTED_NUMBER__", JsonSerializer.Serialize(expectedNumber), StringComparison.Ordinal);

    // Read-only detail page parser, strictly bound to the expected listing ID.
    // No clicking or submitting. Unconfirmed values stay blank.
    public const string ExtractDetailScript = """
((expected) => {
  const norm=s=>String(s||'').replace(/\s+/g,' ').trim();
  const clean=s=>norm(s).replace(/[\s:*：()]/g,'');
  const safeUrl=()=>{try{const u=new URL(location.href);return u.protocol==='https:' &&
    (u.hostname==='itemmania.com'||u.hostname.endsWith('.itemmania.com'));}catch{return false;}};
  const empty={listingNumber:'',game:'',server:'',quantity:'',price:''};
  if(!safeUrl()||!/^[0-9]{8,20}$/.test(expected))return empty;
  const idInUrl=(location.pathname+location.search).includes(expected);
  const idInDom=[...document.querySelectorAll('[data-tid],[data-listing-id],[data-goods-id]')]
    .some(el=>['data-tid','data-listing-id','data-goods-id']
      .some(a=>el.getAttribute(a)===expected));
  if(!idInUrl&&!idInDom)return empty;
  const names={game:['게임','게임명'],server:['서버','서버명'],
    quantity:['수량','판매수량','구매수량','분할수량','보유수량'],
    price:['가격','금액','판매금액','구매금액','분할금액','단가']};
  const num=(v,type)=>{
    const m=norm(v).match(type==='price'
      ? /(?:₩\s*)?([0-9][0-9,]*)\s*(?:원|KRW)?/i
      : /([0-9][0-9,]*)\s*(?:개|건|만|억|조)?/);
    return m?m[1]:'';
  };
  const result={...empty,listingNumber:expected};
  for(const [key,labels] of Object.entries(names)) {
    const matches=[];
    for(const el of document.querySelectorAll('[data-label],dt,th,label,strong,b,span')) {
      const label=clean(el.getAttribute('data-label')||el.textContent);
      if(!labels.includes(label))continue;
      const value=el.getAttribute('data-label')
        ? norm(el.textContent) : norm(el.nextElementSibling?.textContent);
      if(!value)continue;
      const parsed=key==='price'||key==='quantity'?num(value,key):value;
      if(parsed && parsed.length<100)matches.push(parsed);
    }
    const attrs={game:['data-game','data-game-name'],server:['data-server','data-server-name'],
      quantity:['data-quantity','data-qty'],price:['data-price','data-amount']}[key];
    for(const el of document.querySelectorAll(attrs.map(a=>'['+a+']').join(',')))
      for(const attr of attrs){const val=el.getAttribute(attr);if(val) {
        const parsed=key==='price'||key==='quantity'?num(val,key):norm(val);
        if(parsed&&parsed.length<100)matches.push(parsed);
      }}
    // Existing-edit pages may carry values in prefilled controls instead
    // of visible text. Read exact field-name aliases, not broad guesses.
    const formAliases={game:/^(gameName|game_name)$/i,server:/^(serverName|server_name)$/i,
      quantity:/^(quantity|qty|goods_qty|goods_quantity)$/i,
      price:/^(price|goods_price|sale_price|buy_price)$/i};
    for(const el of document.querySelectorAll('input,select')) {
      const name=el.getAttribute('name')||el.id||'';
      if(!formAliases[key].test(name))continue;
      const raw=el.tagName==='SELECT'
        ? (el.selectedOptions?.[0]?.textContent||'') : (el.value||'');
      const parsed=key==='price'||key==='quantity'?num(raw,key):norm(raw);
      if(parsed&&parsed.length<100)matches.push(parsed);
    }
    const unique=[...new Set(matches)];
    if(unique.length===1)result[key]=unique[0];
  }
  const servers=['루페온','실리안','아만','카마인','카제로스','아브렐슈드','카단','니나브'];
  const category=[...document.querySelectorAll('[class*="breadcrumb"],[class*="category"]')]
    .map(e=>norm(e.textContent)).filter(t=>t.includes('로스트아크'));
  const combined=category.join(' | ');
  const matching=servers.filter(v=>combined.includes(v));
  if(combined.includes('로스트아크')&&matching.length===1) {
    if(!result.game)result.game='로스트아크';
    if(!result.server)result.server=matching[0];
  }
  return result;
})(__EXPECTED_NUMBER__)
""";
}
