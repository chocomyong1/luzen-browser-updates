using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuzenBrowserReplica;

// Verified listing identity is deliberately stricter than a generic "success" toast.
// This module reads the currently signed-in page; it never requests private APIs.
internal sealed class ListingIdentityEvidence
{
    public string Number { get; set; } = "";
    public string DetailUrl { get; set; } = "";
    public string Source { get; set; } = "";
    public string Error { get; set; } = "";
    public bool TitlePresent { get; set; }
    public bool DetailPage { get; set; }
}

internal static class ListingIdentity
{
    public static bool HasNumber(string? number) =>
        Regex.IsMatch(number ?? "", @"^\d{8,20}$", RegexOptions.CultureInvariant);

    public static ListingIdentityEvidence Decode(string? json)
    {
        try { return JsonSerializer.Deserialize<ListingIdentityEvidence>(json ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ListingIdentityEvidence { Error = "빈 응답" }; }
        catch (JsonException) { return new ListingIdentityEvidence { Error = "등록번호 결과 파싱 오류" }; }
    }

    public static bool IsConfirmedNew(ListingIdentityEvidence e, ListingJob job) =>
        HasNumber(e.Number) && e.TitlePresent && string.IsNullOrWhiteSpace(e.Error) &&
        (string.IsNullOrWhiteSpace(e.DetailUrl) || MyListingsImport.IsOfficialUrl(e.DetailUrl)) &&
        (string.IsNullOrWhiteSpace(job.ListingNumber) || string.Equals(job.ListingNumber, e.Number, StringComparison.Ordinal));

    public static bool IsConfirmedExisting(ListingIdentityEvidence e, ListingJob job) =>
        HasNumber(job.ListingNumber) && HasNumber(e.Number) &&
        string.Equals(e.Number, job.ListingNumber, StringComparison.Ordinal) &&
        e.DetailPage && string.IsNullOrWhiteSpace(e.Error) &&
        // A title mismatch is not a veto when the official detail URL
        // explicitly carries this exact listing ID. Otherwise use the
        // authenticated own-list fallback instead of guessing.
        (e.TitlePresent || e.Source.Split(',').Contains("page-url", StringComparer.Ordinal));

    // Only official URLs and listing-scoped identifiers are read. Never infer
    // a target from an unrelated number elsewhere in a My Room list.
    public const string BrowserScript = """
    (() => {
      const arg = __ARG__;
      const norm = s => String(s||'').replace(/\s+/g,'').toLowerCase();
      const valid = x => /^\d{8,20}$/.test(String(x||''));
      const official = url => {try {const u=new URL(url,location.href);return u.protocol==='https:' &&
        (u.hostname==='itemmania.com'||u.hostname.endsWith('.itemmania.com'))} catch {return false}};
      const expected = norm(arg.title), expectedNumber=String(arg.number||'').trim();
      const text = (document.body?.innerText||'').slice(0,100000);
      // A successful new listing can be confirmed only if its actual title appears.
      // A pure number on a public list or an unrelated page is not sufficient.
      const titlePresent = expected.length>=2 && norm(text+' '+document.title).includes(expected);
      const keys = ['tid','trade_id','tradeid','trade_no','tradeno','goods_id','goods_no','goodsno',
        'goods_idx','listing_id','listingid','item_no','itemno','item_id','article_no','sale_no','buy_no'];
      const idsFromUrl = link => {try {
        const u=new URL(link,location.href);if(!official(u.href))return [];
        const ids=[];
        for(const [k,v] of u.searchParams) if(keys.includes(k.toLowerCase())&&valid(v)) ids.push(v);
        // Some authenticated detail pages encode the registration number as
        // a full numeric URL path segment instead of a query parameter.
        for(const segment of u.pathname.split('/')) if(valid(segment)) ids.push(segment);
        return [...new Set(ids)];
      }catch{return []}};
      const pathHints = /(?:myroom|detail|view|goods|item|trade|modify|sell|buy|regist)/i;
      const currentUrl = location.href, currentIds=idsFromUrl(currentUrl);
      const ownList = /^\/myroom\/(?:sell\/sell_regist|buy\/buy_regist)\.html$/i.test(location.pathname);
      const sameUrl=(a,b)=>{try {const x=new URL(a,location.href),y=new URL(b,location.href);
        x.hash='';y.hash='';return x.href===y.href;}catch{return false}};
      // Only a URL returned for THIS imported listing can serve as an
      // additional binding. An arbitrary listing number on a list is unsafe.
      const ownEntry=!!arg.entryUrl && official(arg.entryUrl) && pathHints.test(new URL(arg.entryUrl).pathname);
      const entryBound=valid(expectedNumber)&&!ownList&&ownEntry&&sameUrl(currentUrl,arg.entryUrl)&&
        (currentIds.includes(expectedNumber) ||
         new RegExp('(?:^|\\D)'+expectedNumber+'(?:\\D|$)').test(new URL(arg.entryUrl).pathname+new URL(arg.entryUrl).search));
      const numbers = new Map();
      const add = (num,src) => {num=String(num||'').trim();if(!valid(num))return;
        if(!numbers.has(num))numbers.set(num,new Set());numbers.get(num).add(src);};
      const labeled = /(?:등록\s*번호|물품\s*번호|상품\s*번호|거래\s*번호)\s*[:：#]?\s*(\d{8,20})(?!\d)/g;
      for(const m of text.matchAll(labeled)) add(m[1],'label');
      const attrs = ['data-tid','data-listing-id','data-goods-id','data-goods-no','data-trade-id'];
      for(const a of attrs) {
        const nodes = [...document.querySelectorAll('['+a+']')];
        // Do not accept an ID hidden in an unrelated table/list of other listings.
        if(nodes.length===1) add(nodes[0].getAttribute(a),'attribute');
      }
      // Hidden fields are permitted as evidence only when there is one ID
      // and the field name explicitly refers to this listing/trade.
      for(const name of keys) {
        const els=[...document.querySelectorAll('input[type="hidden"][name]')]
          .filter(e=>e.name?.toLowerCase()===name);
        if(els.length===1)add(els[0].value,'hidden-id');
      }
      if(official(currentUrl) && pathHints.test(location.pathname))
        for(const id of currentIds)add(id,'page-url');
      const pairs=[];
      for(const a of document.querySelectorAll('a[href]')){
        if(!official(a.href))continue;
        const ids=idsFromUrl(a.href);
        if(ids.length!==1)continue;
        const u=new URL(a.href);
        if(pathHints.test(u.pathname))
          pairs.push({number:ids[0],url:u.href.split('#')[0]});
      }
      // A link pointing to the same ID is supporting evidence, not a random guess.
      if(numbers.size===0 && pairs.length===1 && titlePresent) add(pairs[0].number,'detail-link');
      // An exact matching current detail URL takes precedence over unrelated
      // IDs in linked recommendations, but never over a contradictory labelled
      // registration number. A title match is only supporting evidence for
      // a detail URL already independently bound to this listing ID.
      const labels=[...text.matchAll(labeled)].map(m=>m[1]);
      const contradicts=labels.some(id=>valid(expectedNumber)&&id!==expectedNumber);
      const urlBound=!ownList && currentIds.length===1 && currentIds[0]===expectedNumber &&
        pathHints.test(location.pathname);
      if((entryBound||urlBound) && titlePresent && !contradicts)add(expectedNumber,'own-listing-url');
      const ids=[...numbers.keys()];
      const bound=(entryBound||urlBound) && titlePresent && !contradicts &&
        (!currentIds.length || currentIds.every(id=>id===expectedNumber));
      const id=bound ? expectedNumber : ids.length===1?ids[0]:'';
      if(!id)return {number:'',detailUrl:'',source:'',error:
        contradicts?'현재 페이지의 등록번호가 대상 글과 다릅니다':
        ids.length>1?'현재 페이지에서 여러 등록번호가 보여 대상 글을 확인할 수 없습니다':
        '등록번호를 확정할 수 없습니다 (대상 상세/관리 URL에 번호가 없거나 페이지가 아직 로드되지 않음)',
        titlePresent,detailPage:false};
      const links=[...new Set(pairs.filter(x=>x.number===id).map(x=>x.url))];
      let detailUrl=links.length===1?links[0]:'';
      // The current detail URL is usable only when the address explicitly carries the ID.
      if(!detailUrl && (currentIds.includes(id)||bound) && official(currentUrl)) detailUrl=currentUrl.split('#')[0];
      const detailPage = !ownList && ((currentIds.includes(id) && pathHints.test(location.pathname)) || bound ||
        /(?:등록번호|물품번호|상품번호|거래번호)\s*[:：#]?\s*\d{8,20}/.test(text) ||
        [...document.querySelectorAll('[data-tid],[data-listing-id]')].some(x=>(x.dataset.tid||x.dataset.listingId||'')===id));
      return {number:id,detailUrl,source:[...(numbers.get(id)||[])].join(',')+(bound?',url-bound':''),
        error:contradicts?'현재 페이지에 대상 글과 다른 등록번호가 표시됩니다':'',titlePresent,detailPage};
    })()
    """;
}
