'use strict';
// The old manual editor remains as a recovery/diagnostic path; the newly
// approved single and checked-list actions use the verified native click.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const app = read('AutomationForm.SitePriceEdit.cs').replace(/\r\n/g, '\n');
const grid = read('AutomationForm.cs');
const site = read('SitePriceEdit.cs');
const observer = read('SitePriceSubmitObserver.cs');

assert.match(app, /private DialogResult ShowVisibleSinglePriceEditor\(/);
assert.match(app, /originalParent\.Controls\.Remove\(_web\)/);
assert.match(app, /browserHost\.Controls\.Add\(_web\)/);
assert.match(app, /return window\.ShowDialog\(this\)/);
assert.match(app, /originalParent\.Controls\.Add\(_web\)/);
assert.match(app, /_web\.Bounds = originalBounds/);
assert.match(app, /_visibleSitePriceEditWindow = null/);
assert.match(app, /"사이트의 \[재등록\]을 직접 누르세요/);
assert.match(app, /"사이트 제출 후 결과 확인"/);
assert.match(app, /if \(!observer\.FinalPostStarted\) await Task\.Delay/);
assert.match(app, /if \(!observer\.FinalPostStarted\)[\s\S]*?"사이트의 최종 가격 수정 요청이 아직 확인되지 않았습니다/);
assert.match(observer, /public bool FinalPostStarted => _requestId\.Length > 0/);

const singleBranch = app.slice(app.indexOf('if (nativeSingleApproval && !backgroundApproved)'),
  app.indexOf('\n                else\n                {\n                    // Explicit single-item'));
assert(singleBranch.length > 500, 'single-item visible branch must be present');
assert.match(singleBranch, /job\.RequiresReview = true/);
assert.match(singleBranch, /ShowVisibleSinglePriceEditor\(job, current, desired, submissionObserver\)/);
assert.match(singleBranch, /SitePriceEdit\.NativePriceButtonClickedScript/);
assert.match(singleBranch, /if \(!siteButtonClicked && !submissionObserver\.FinalPostStarted\)/);
assert.match(singleBranch, /submitted = false/);
assert(singleBranch.indexOf('if (!siteButtonClicked && !submissionObserver.FinalPostStarted)') <
  singleBranch.indexOf('HourlyServerQuota.MarkAttempt('),
  'Canceled visible editing must not consume the posting quota');
assert.doesNotMatch(singleBranch, /DispatchVerifiedSitePriceClickAsync|mousePressed|\.submit\(|\.click\(/);
assert.match(app, /backgroundApproved: true\);/);
assert.match(app, /_unattendedSitePriceEdit = true;\s*await DispatchVerifiedSitePriceClickAsync/);

assert.match(grid, /if \(_visibleSitePriceEditWindow is not null\)[\s\S]*?HandleVisibleSitePriceDialog\(e\)/);
assert.match(app, /MessageBox\.Show\(window, message, "아이템매니아 안내"/);
assert.match(app, /MessageBox\.Show\(window, message, "아이템매니아 확인", MessageBoxButtons\.YesNo/);
assert.match(site, /NativePriceButtonClickedScript/);
assert.match(site, /p\.money\(p\.priceInput\.value\)!==p\.desired\|\|/);
assert.match(site, /p\.titleChanged\(\)\|\|!otherFieldsUnchanged/);
assert.match(site, /e\.preventDefault\(\);e\.stopImmediatePropagation\(\)/);
assert.match(site, /p\.form\.addEventListener\('submit',protectTitleAtSubmit,\{capture:true\}\)/);
assert.match(site, /p\.form\.addEventListener\('submit',protectTitleAtSubmit,\{capture:false\}\)/);
assert.match(app, /page\.Listings\.Where\(x => x\.ListingNumber == job\.ListingNumber/);
assert.doesNotMatch(singleBranch, /BuildOwnedTitleScript/);
console.log('PASS visible single-listing official price editor, exact-ID selection, and result observation');
