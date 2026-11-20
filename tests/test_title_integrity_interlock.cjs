// Cross-checks the C# call sites that surround the separately executed
// browser-script mocks. No account, login, or live site writes are used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');
const form = source('AutomationForm.cs');
const sitePriceForm = source('AutomationForm.SitePriceEdit.cs');
const sitePriceScript = source('SitePriceEdit.cs');
const priceExecutable = sitePriceScript.replace(/\/\/[^\n]*/g, '');
const importer = source('MyListingsImport.cs');
const models = source('AutomationModels.cs');
const tables = source('AutomationForm.ModernTables.cs');
const repost = source('OwnedListRepost.cs');
const reservations = source('AutomationForm.PriceReservations.cs');

const startup = form.match(/if \(oldJob\.ImportedFromSite\)\s*\{([\s\S]*?)\n\s{12}\}/)?.[1];
assert(startup, 'startup imported-job block must exist');
assert.doesNotMatch(startup, /oldJob\.Title\s*=/,
  'loading the saved configuration must not silently rewrite a historical title');
assert.match(startup, /HasSuspiciousDuplicatedLevel\(oldJob\.Title\)/);
assert.match(startup, /oldJob\.RequiresReview\s*=\s*true/);
assert.match(form, /!string\.IsNullOrWhiteSpace\(oldJob\.PendingSitePriceTarget\) && !oldJob\.RequiresReview/);
assert.match(form, /oldJob\.LastStatus = "제목 보호 모드: 가격 예약 자동 제출 중지"/);

assert.match(sitePriceScript, /AutomaticPriceSubmissionEnabled\s*=>\s*false/);
assert.doesNotMatch(priceExecutable, /\bfetch\s*\(/,
  'never run a synthetic full-form price repost (including dead branches)');
assert.doesNotMatch(priceExecutable, /\bnew\s+FormData\s*\(/);
assert.match(sitePriceScript, /return result\('blocked','제목 보호 모드/);
assert.match(sitePriceForm, /if \(!SitePriceEdit\.AutomaticPriceSubmissionEnabled\)/);
assert.match(reservations, /제목 보호 모드 · 사이트 가격 자동 제출 중지/);

assert.match(repost, /if\(!matching\[0\]\.identity\.titleMatches/);
assert.match(repost, /return result\('unsafe-title'/);
assert.match(form, /if \(ownRow\.Status == "unsafe-title"\)/);
assert.match(form, /catch \(SiteTitleSafetyException ex\) when \(!submitted && !dryRun\)/);
assert.match(form, /job\.Enabled\s*=\s*false;\s*job\.RequiresReview\s*=\s*true;/);

assert.match(importer, /SiteObservedTitle = Clean\(row\.Title\)/);
assert.match(importer, /Target\.SiteObservedTitle = SiteObservedTitle/);
assert.match(importer, /RequiresReview = suspiciousTitle \|\| !CanTrustOwnedImport/);
assert.match(models, /public bool SiteTitleDiffers/);
assert.match(tables, /job\.SiteTitleDiffers/);
assert.match(tables, /사이트: " \+ job\.SiteObservedTitle/);

console.log('PASS title safety at config startup, import, visual diff, repost, and price-queue boundaries');
