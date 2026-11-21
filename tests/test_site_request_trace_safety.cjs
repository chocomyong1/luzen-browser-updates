'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '../AppSource', file), 'utf8');
const trace = read('AutomationForm.SiteRequestTrace.cs');
const ui = read('AutomationForm.cs');
const main = read('MainForm.cs');
const price = read('AutomationForm.SitePriceEdit.cs');
const observer = read('SitePriceSubmitObserver.cs');

assert.match(main, /traceMenuItem\.Click \+= async .*ToggleSiteTraceFromHeaderAsync\(traceMenuItem\)/);
assert.match(main, /moreMenu\.Items\.Add\(traceMenuItem\)/);
assert.match(ui, /ToggleSiteTraceFromHeaderAsync\(ToolStripItem menuItem\) => ToggleSiteRequestTraceAsync\(menuItem\)/);
assert.match(trace, /ToggleSiteRequestTraceAsync\(ToolStripItem button\)/);
assert.match(trace, /GetDevToolsProtocolEventReceiver\("Network\.requestWillBeSent"\)/);
assert.match(trace, /GetDevToolsProtocolEventReceiver\("Network\.responseReceived"\)/);
assert.match(trace, /Network\.getRequestPostData/);
assert.match(trace, /requestId/);
assert.match(trace, /RequestTraceEntry|SiteRequestTraceEntry/);
assert.match(trace, /TraceFieldNames\(method == "GET" \? uri\.Query\.TrimStart/);
assert.match(trace, /GetTraceString\(req, "postData"\)/);
assert.match(trace, /item\.FieldNames/);
assert.match(trace, /\.AbsolutePath/);
assert.match(trace, /!ItemManiaAccountStore\.IsOfficialSite\(uri\)/);
assert.match(trace, /method is not \("GET" or "POST" or "PUT" or "PATCH"\)/);
assert.match(trace, /method == "GET" && !Regex\.IsMatch/);
assert.match(trace, /pass\|cookie\|token\|secret\|auth\|csrf\|session/);
assert.match(trace, /Content-Disposition:/);
assert.match(trace, /match\.Groups\[1\]\.Value/);
assert.match(trace, /multipart \? "멀티파트 필드 이름 미확인"/);
assert.doesNotMatch(trace, /Clipboard\.SetText\(e\.ParameterObjectAsJson|AppendLog\(.*postData|request\.Headers/);
assert.doesNotMatch(trace, /Network\.setRequestInterception|Fetch\.enable|\.click\(\)|\.submit\(\)/);
assert.match(trace, /_web\.Bounds = new Rectangle\(12, 49,/);
assert.match(trace, /_web\.Bounds = new Rectangle\(-20000, -20000, 1280, 900\)/);
assert.match(observer, /BuyReRegistrationPostPath = "\/myroom\/buy\/buy_re_reg_ok\.php"/);
assert.match(observer, /Network\.requestWillBeSent/);
assert.match(observer, /Network\.responseReceived/);
assert.match(observer, /Network\.loadingFinished/);
assert.match(observer, /Network\.loadingFailed/);
assert.match(observer, /OtherBuyPostPath/);
assert.match(observer, /StartsWith\("\/myroom\/buy\/"/);
assert.match(price, /observed\.OtherBuyPostPath/);
assert.doesNotMatch(observer, /getRequestPostData|\.postData|\.headers|\.response\.body|Fetch\.enable/);
const safetyStart = price.indexOf('using var submissionObserver = new SitePriceSubmitObserver(');
const siteSubmit = price.indexOf('BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true,');
const finish = price.indexOf('await submissionObserver.WaitAsync(token)', siteSubmit);
const readback = price.indexOf('await ConfirmChangedSitePriceAsync(job, desired, token, siteTitleSnapshot)', siteSubmit);
const finalVerify = price.indexOf('SitePriceEdit.FinalizeNativePriceScript', siteSubmit);
const preClick = price.indexOf('await DispatchVerifiedSitePriceClickAsync(final.X, final.Y, token, () =>', finalVerify);
const checkpoint = price.indexOf('job.RequiresReview = true', preClick);
assert(safetyStart >= 0 && safetyStart < siteSubmit && siteSubmit < finalVerify &&
  finalVerify < preClick && preClick < checkpoint && checkpoint < finish,
  'No review checkpoint or quota attempt before verified price input and button checks');
assert(siteSubmit < finish && finish < readback,
  'The site POST must finish or time out before another page navigation starts');
assert.match(price.slice(checkpoint, finish), /_config\.Save\(\)[\s\S]*?submitted = true;/,
  'Crash-safe review checkpoint must be persisted before the browser mouse press');
assert.match(price, /stage = "클릭 직전 결과 확인 대기 상태 저장";\s*checkpointBeforeMousePress\(\);\s*stage = "재등록 버튼 마우스 누름";\s*await MouseAsync\("mousePressed"/);
assert.doesNotMatch(price.slice(finish, readback), /if \(!observed\.RequestObserved\)[\s\S]*?throw/,
  'A missing POST observer event must NOT bypass read-only own-list verification');
console.log('PASS passive request trace, crash-safe hold and real readback even when POST detection fails');
