'use strict';
// ID-only site price target; the actual site title is preserved, not used as
// an identity condition. No network or account access is involved here.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname,'../AppSource',file),'utf8');
const automation = read('AutomationForm.SitePriceEdit.cs');
const models = read('AutomationModels.cs');
const script = read('SitePriceEdit.cs');
const main = read('AutomationForm.cs');
const submitFlow = automation.slice(automation.indexOf('private async Task<bool> ApplySitePriceAsync('),
  automation.indexOf('private sealed class SitePriceReadback'));
const readback = automation.slice(automation.indexOf('private async Task<SitePriceReadback> ReadSitePriceAfterRepostAsync('),
  automation.indexOf('private void HandleSitePriceEditScriptDialog('));

assert.match(submitFlow,/LocateOwnedListRepostAsync\(job, token, requireRepostButton: false\)/);
assert.match(submitFlow,/records = page\.Listings\.Where\(x => x\.ListingNumber == job\.ListingNumber &&\s*x\.TradeSide == job\.OriginalTradeSide/);
assert.match(submitFlow,/records\.Count != 1 \|\| !TrySitePrice\(records\[0\]\.Price, out var current\)/);
assert.match(submitFlow,/string siteTitleSnapshot = records\[0\]\.Title;/);
assert.doesNotMatch(submitFlow,/PriceReadbackTitle\(records\[0\]\.Title\)\s*!=\s*PriceReadbackTitle\(job\.Title\)/);
assert.doesNotMatch(submitFlow,/HasSuspiciousDuplicatedLevel\(job\.Title\)/);
assert.match(submitFlow,/BuildOwnedTitleScript\(job\.ListingNumber, siteTitleSnapshot, false/);
assert.match(submitFlow,/BuildOwnedTitleScript\(job\.ListingNumber, siteTitleSnapshot, true/);
assert.match(submitFlow,/BuildEditFormScript\(job\.ListingNumber, current\.ToString\(\), desired\.ToString\(\), false,\s*siteTitleSnapshot/);
assert.match(submitFlow,/BuildEditFormScript\(job\.ListingNumber, current\.ToString\(\), desired\.ToString\(\), true,\s*siteTitleSnapshot/);
assert.match(submitFlow,/ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
assert.match(submitFlow,/job\.PriceEditSiteTitleSnapshot = siteTitleSnapshot;/);
assert.match(models,/public string PriceEditSiteTitleSnapshot \{ get; set; \} = ""/);
assert.match(main,/copy\.PriceEditSiteTitleSnapshot = ""/);
assert.match(main,/job\.PriceEditSiteTitleSnapshot = ""/);
assert.match(readback,/string titleBeforeSubmit = !string\.IsNullOrWhiteSpace\(titleSnapshot\) \? titleSnapshot/);
assert.match(readback,/job\.PriceEditSiteTitleSnapshot/);
assert.match(readback,/PriceReadbackTitle\(matchingOld\[0\]\.Title\) != expectedTitle/);
assert.match(readback,/legacyTitleFallback && MyListingsImport\.HasSuspiciousDuplicatedLevel/);
assert.match(script,/AutomaticPriceSubmissionEnabled\s*=>\s*false/);
assert.match(script,/if\(p\.titleChanged\(\)\)\{/);
const submissionScripts=script.slice(script.indexOf('public const string VerifyNativePriceInputScript'));
assert.doesNotMatch(submissionScripts.replace(/\/\/[^\n]*/g,''),/\bfetch\s*\(|\bnew\s+FormData\s*\(/);
console.log('PASS exact listing ID identifies price target; fresh site title is preserved and checked after submission');
