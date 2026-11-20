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
assert.match(script, /siteSubmit\[0\]\.click\(\)/);
assert.match(script, /mayRestoreTitle=!titlePreserved&&a\.nativeSingleApproval===true/);
assert.match(script, /quantity\(rangeTitle\[1\]\)===verifiedMin&&quantity\(rangeTitle\[2\]\)===verifiedMax/);
assert.match(script, /titleSignature\(rangeTitle\[3\]\)===titleSignature\(savedLevel\[2\]\)/);
assert.match(script, /if\(mayRestoreTitle&&!a\.allowVerifiedTitleRestore\)return result\('restore-needed'/);
assert.match(script, /if\(mayRestoreTitle\)titleInputs\[0\]\.value=a\.expectedTitle/);
assert.match(script, /form\.addEventListener\('submit'/);
assert.match(script, /e\.preventDefault\(\)/);
assert.match(script, /others\.some\(o=>String\(o\.element\.value\?\?''\)!==o\.value\)/);
assert.match(script, /titleSignature\(titleInputs\[0\]\.value\)!==titleSignature\(originalTitle\)/);
assert.match(script, /titleSignature\(siteTitle\)===titleSignature\(originalTitle\)/);
assert.match(script, /사이트 수정폼: .*displayTitle\(siteTitle\)/);
assert.doesNotMatch(script.replace(/\/\/[^\n]*/g,''), /\bfetch\s*\(|\bnew\s+FormData\s*\(/);
assert.match(app, /nativeSingleApproval && \(fromScheduler \|\| bulkApproval \|\| manuallyApprovedPrice is null\)/);
assert.match(app, /!SitePriceEdit\.AutomaticPriceSubmissionEnabled && !nativeSingleApproval/);
assert.match(app, /_jobs\.Any\(other => !ReferenceEquals\(other, job\)/);
assert.match(app, /!string\.IsNullOrWhiteSpace\(job\.PendingSitePriceTarget\)/);
assert.match(app, /SitePriceEdit\.BuildEditFormScript\(job\.ListingNumber, current\.ToString\(\), desired\.ToString\(\), true,\s*job\.Title, nativeSingleApproval, records\[0\]\.MinQuantity, records\[0\]\.MaxQuantity/);
assert.match(app, /ConfirmChangedSitePriceAsync\(job, desired, token\)/);
assert.match(app, /form\.Status == "restore-needed" && nativeSingleApproval/);
assert.match(app, /MessageBoxDefaultButton\.Button2/);
assert.match(app, /records\[0\]\.MinQuantity, records\[0\]\.MaxQuantity/);
assert.match(app, /job\.Price = desired\.ToString\("N0"\)/);
assert.match(editor, /사이트 구매글 가격 변경 \(한 건 시험\)/);
assert.match(editor, /async \(_, _\) => await ApplySelectedSitePriceAsync\(\)/);
assert.doesNotMatch(editor, /ApplySitePriceAsync\(job, bulkApproval: true, fromScheduler: true, nativeSingleApproval:/);

console.log('PASS native single-price pilot requires direct consent, verified ID, unchanged title, native click and readback; schedule locked');
