// Cross-checks the C# call sites that surround the separately executed
// browser-script mocks. No account, login, or live site writes are used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');
const form = source('AutomationForm.cs');
const sitePriceForm = source('AutomationForm.SitePriceEdit.cs');
const sitePriceScript = source('SitePriceEdit.cs');
// The separate read-only diagnostic may GET a referenced same-origin .js
// asset; no live price submission script may synthesize a form POST.
const priceExecutable = sitePriceScript.slice(sitePriceScript.indexOf('public const string VerifyNativePriceInputScript'))
  .replace(/\/\/[^\n]*/g, '');
const importer = source('MyListingsImport.cs');
const models = source('AutomationModels.cs');
const tables = source('AutomationForm.ModernTables.cs');
const repost = source('OwnedListRepost.cs');
const reservations = source('AutomationForm.PriceReservations.cs');

assert.match(form, /MyListingsImport\.IsPreSubmitTitleOnlyHold\(oldJob\)/);
assert.doesNotMatch(form, /oldJob\.Title\s*=/,
  'loading the saved configuration must never rewrite the title');
assert.match(form, /oldJob\.RequiresReview\s*=\s*false/);
assert.match(form, /oldJob\.Enabled\s*=\s*false/,
  'clearing a false title hold must not silently enable auto-posting');
assert.match(form, /!string\.IsNullOrWhiteSpace\(oldJob\.PendingSitePriceTarget\) &&/);
assert.match(form, /oldJob\.LastStatus == "제목 보호 모드: 가격 예약 자동 제출 중지"/);
assert.match(form, /!oldJob\.PendingRepostAttemptUtc\.HasValue/);
assert.match(form, /oldJob\.LastStatus = "제목 보호 모드: 가격 예약 자동 제출 중지"/);

assert.match(sitePriceScript, /AutomaticPriceSubmissionEnabled\s*=>\s*false/);
assert.doesNotMatch(priceExecutable, /\bfetch\s*\(/,
  'never run a synthetic full-form price repost (including dead branches)');
assert.doesNotMatch(priceExecutable, /\bnew\s+FormData\s*\(/);
assert.match(sitePriceScript, /return result\('blocked','제목 보호 모드/);
assert.match(sitePriceForm, /if \(!SitePriceEdit\.AutomaticPriceSubmissionEnabled\)/);
assert.match(reservations, /제목 보호 모드 · 사이트 가격 자동 제출 중지/);

assert.doesNotMatch(repost, /if\(!matching\[0\]\.identity\.titleMatches/);
assert.doesNotMatch(repost, /return result\('unsafe-title'/);
assert.doesNotMatch(form, /if \(ownRow\.Status == "unsafe-title"\)/);
assert.match(repost, /if\(!ids\.has\(arg\.number\)\|\|!identity\.ok\|\|!strongNumber\)continue/);
assert.match(repost, /if\(matching\[0\]\.actions\.length!==1\)/);

assert.match(importer, /SiteObservedTitle = Clean\(row\.Title\)/);
assert.match(importer, /Target\.SiteObservedTitle = SiteObservedTitle/);
assert.match(importer, /RequiresReview = !CanTrustOwnedImport/);
assert.match(importer, /internal static bool IsPreSubmitTitleOnlyHold\(ListingJob job\)/);
assert.match(importer, /!job\.PendingRepostAttemptUtc\.HasValue/);
assert.match(importer, /string\.IsNullOrWhiteSpace\(job\.PriceEditSiteTitleSnapshot\)/);
assert.match(models, /public bool SiteTitleDiffers/);
assert.match(tables, /job\.SiteTitleDiffers/);
assert.match(tables, /사이트: " \+ job\.SiteObservedTitle/);

console.log('PASS ID-bound repost, title-only legacy hold cleanup, original title preservation, and price-queue boundaries');
