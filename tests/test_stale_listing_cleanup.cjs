// Static enforcement of the no-site-delete stale-listing reconciliation flow.
// The actual stale-ID planner is executed by ImportSyncTests on Windows .NET.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = file => fs.readFileSync(path.join(__dirname, '..', 'AppSource', file), 'utf8');
const importer = app('MyListingsImport.cs');
const owned = app('AutomationForm.MyListings.cs');
const main = app('AutomationForm.cs');

assert.match(importer, /FindStaleImportedJobs\(/);
assert.match(importer, /!completedAllPages \|\| string\.IsNullOrWhiteSpace\(siteId\)/);
assert.match(importer, /!sides\.Contains\("판매"\) \|\| !sides\.Contains\("구매"\)/);
assert.match(importer, /job\.ImportedFromSite && job\.Operation == "재등록"/);
assert.match(importer, /!job\.PendingRepostAttemptUtc\.HasValue/);
assert.match(importer, /IsOwnedListUrl\(job\.SourceListUrl\)/);
assert.match(importer, /observed\.Contains\(job\.OriginalTradeSide \+ ":" \+ job\.ListingNumber\.Trim\(\)\)/);

assert.match(owned, /VerifiedTradeSides\.Add\(/);
assert.match(owned, /IsSellListUrl\(page\.PageUrl\) != MyListingsImport\.IsSellListUrl\(url\)/);
assert.match(owned, /scan\.CompletedAllPages && !scan\.LoginRequired && scan\.NavigationFailures == 0/);
assert.match(owned, /stale\.Count == 0/);
assert.match(owned, /BackupStaleImportedJobs\(account\.UserId, stale\)/);
assert.match(owned, /new FileStream\(path, FileMode\.CreateNew/);
assert.match(owned, /_config\.Save\(\); \}\s*catch \{ _config\.Jobs = priorConfigJobs; throw; \}/);
assert.match(owned, /foreach \(var old in stale\) _jobs\.Remove\(old\)/);
assert.match(main, /정리한 예전 글 복구/);
assert.match(owned, /job\.Enabled = false;\s*job\.RequiresReview = true/);
assert.match(owned, /백업의 시스템 또는 아이템매니아 계정이 현재 설정과 다릅니다/);

console.log('PASS stale IDs require complete two-sided scan, user confirmation, backup, and safe restore');
