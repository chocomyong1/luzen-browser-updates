'use strict';
// Safe staged release: one user-confirmed item can use only the exact native
// site button. The scheduler and any prior queued batch remain hard-locked.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const script = read('SitePriceEdit.cs');
const app = read('AutomationForm.SitePriceEdit.cs');
const editor = read('AutomationForm.cs');

assert.match(script, /AutomaticPriceSubmissionEnabled\s*=>\s*false/);
assert.match(script, /a\.nativeSingleApproval===true/);
assert.doesNotMatch(script, /siteSubmit\[0\]\.click\(\)/);
assert.match(script, /a\.trustedOneClick!==true/);
assert.match(script, /document\.elementFromPoint/);
assert.match(script, /return result\('armed'/);
assert.match(script, /VerifyNativeClickScript/);
assert.match(script, /p\.canClick=\(\)=>p\.canType\(\)/);
assert.match(script, /VerifyNativePriceInputScript/);
assert.match(script, /VerifyNativePriceFocusScript/);
assert.match(script, /SelectNativePriceForReplacementScript/);
assert.match(script, /return result\('input-mismatch'/);
assert.match(script, /FinalizeNativePriceScript/);
const nativeFinalizer = script.split('public const string FinalizeNativePriceScript = """')[1]
  ?.split('""";')[0];
assert(nativeFinalizer, 'Native price submit finalizer must be present');
assert.doesNotMatch(nativeFinalizer, /Object\.defineProperty\s*\(\s*e\s*,\s*['"]preventDefault['"]/,
  "Never patch the site's native preventDefault during purchase edit submission");
assert.match(nativeFinalizer, /window\.addEventListener\?\.\('submit',observeSiteSubmit/);
assert.match(script, /mayRestoreTitle=!titlePreserved&&a\.nativeSingleApproval===true/);
assert.match(script, /quantity\(rangeTitle\[1\]\)===verifiedMin&&quantity\(rangeTitle\[2\]\)===verifiedMax/);
assert.match(script, /titleSignature\(rangeTitle\[3\]\)===titleSignature\(savedLevel\[2\]\)/);
assert.match(script, /if\(mayRestoreTitle&&!a\.allowVerifiedTitleRestore\)return result\('restore-needed'/);
assert.match(script, /if\(mayRestoreTitle\)titleInputs\[0\]\.value=a\.expectedTitle/);
assert.match(script, /p\.form\.addEventListener\('submit'/);
assert.match(script, /e\.preventDefault\(\)/);
assert.match(script, /p\.guardBlocked=true/);
assert.match(script, /p\.submitEvent\?\.defaultPrevented/);
assert.match(script, /changed\.some\(o=>!safePriceField\(o\)\)/);
assert.match(script, /titleSignature\(titleInputs\[0\]\.value\)!==titleSignature\(originalTitle\)/);
assert.match(script, /titleSignature\(siteTitle\)===titleSignature\(originalTitle\)/);
assert.match(script, /사이트 수정폼: .*displayTitle\(siteTitle\)/);
const submissionScripts = script.slice(script.indexOf('public const string VerifyNativePriceInputScript'));
assert.doesNotMatch(submissionScripts.replace(/\/\/[^\n]*/g,''), /\bfetch\s*\(|\bnew\s+FormData\s*\(/);
assert.match(app, /nativeSingleApproval && \(fromScheduler \|\| bulkApproval \|\| manuallyApprovedPrice is null\)/);
assert.match(app, /!SitePriceEdit\.AutomaticPriceSubmissionEnabled && !nativeSingleApproval/);
assert.match(app, /_jobs\.Any\(other => !ReferenceEquals\(other, job\)/);
assert.match(app, /string\.IsNullOrWhiteSpace\(job\.PendingSitePriceTarget\)[\s\S]*?job\.Price : job\.PendingSitePriceTarget/);
assert.match(app, /SitePriceEdit\.BuildEditFormScript\(job\.ListingNumber, current\.ToString\(\), desired\.ToString\(\), true,\s*siteTitleSnapshot, nativeSingleApproval, records\[0\]\.MinQuantity, records\[0\]\.MaxQuantity/);
assert.match(app, /ConfirmChangedSitePriceAsync\(job, desired, token, siteTitleSnapshot\)/);
assert.match(app, /if \(nativeSingleApproval\) job\.PendingSitePriceTarget = desired\.ToString\(\)/);
assert.match(app, /TypeVerifiedSitePriceAsync\(result\.X, result\.Y, desired, token\)/);
assert.match(app, /ReplaceVerifiedSitePriceTextOnceAsync\(desired, token\)/);
assert.match(app, /CallDevToolsProtocolMethodAsync\("Input\.insertText"/);
assert.match(app, /core\.ExecuteScriptAsync\(SitePriceEdit\.VerifyNativePriceFocusScript\)/);
assert.match(app, /DispatchVerifiedSitePriceClickAsync\(final\.X, final\.Y, token, \(\) =>/);
assert.match(app, /CallDevToolsProtocolMethodAsync\("Input\.dispatchMouseEvent"/);
assert.match(app, /core\.ExecuteScriptAsync\(SitePriceEdit\.VerifyNativeClickScript\)/);
assert.match(app, /trustedOneClick: true/);
assert.doesNotMatch(app, /if \(!observed\.RequestObserved\)\s*throw/);
assert.ok(app.indexOf('ConfirmChangedSitePriceAsync(job, desired, token, siteTitleSnapshot)') >
  app.indexOf('var observed = await submissionObserver.WaitAsync(token)'),
  'Even a missing POST event must be followed by read-only OWN-list price/title verification');
assert.match(app, /private async Task RecheckSelectedPriceWithoutSubmitAsync\(\)/);
assert.match(app, /await InspectPendingSitePriceOnlyAsync\(job, \(long\)amount.Value, confirmLocalOnExact: true\)/);
assert.match(app, /job\.RequiresReview = true;\s*job\.Enabled = false;\s*job\.NextDueUtc = DateTime\.MaxValue;\s*job\.LastStatus = "사이트 단가 재조회 확인/);
assert.match(app, /form\.Status == "restore-needed" && nativeSingleApproval/);
assert.match(app, /MessageBoxDefaultButton\.Button2/);
assert.match(app, /records\[0\]\.MinQuantity, records\[0\]\.MaxQuantity/);
assert.match(app, /job\.Price = desired\.ToString\("N0"\)/);
assert.match(editor, /선택 1건 즉시 가격 수정 \(실제 제출\)/);
assert.match(editor, /async \(_, _\) => await ApplySelectedSitePriceAsync\(\)/);
assert.match(editor, /가격 변경 결과 재조회 \(제출 없음\)/);
assert.doesNotMatch(editor, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true, nativeSingleApproval:/);

console.log('PASS native single-price pilot requires direct consent, verified ID, unchanged title, native click and readback; schedule locked');
