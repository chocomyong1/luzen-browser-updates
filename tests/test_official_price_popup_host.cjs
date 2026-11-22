const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const popup = fs.readFileSync(path.join(__dirname, '../AppSource/ItemManiaLogin.cs'), 'utf8');
const form = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const price = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.SitePriceEdit.cs'), 'utf8');

assert.match(form, /InstallSilent\(_web, message => AppendLog\(message\),\s*\(\) => !string\.IsNullOrWhiteSpace\(_armedSitePriceEditNumber\)\)/);
assert.match(popup, /approvedPriceEditInProgress\?\.Invoke\(\) == true/);
assert.match(popup, /view\.Source\?\.AbsolutePath\.Equals\("\/myroom\/buy\/buy_re_reg\.html"/);
assert.match(popup, /args\.GetDeferral\(\)/);
assert.match(popup, /EnsureCoreWebView2Async\(opener\.CoreWebView2\.Environment\)/);
assert.match(popup, /args\.NewWindow = child\.CoreWebView2/);
assert.match(popup, /deferral\.Complete\(\)/);
assert.match(popup, /ItemManiaAccountStore\.IsOfficialSite\(requestedUri\)/);
assert.match(popup, /ItemManiaAccountStore\.IsOfficialSite\(target\)/);
assert.match(popup, /navigation\.Cancel = true/);
assert.match(popup, /AreDefaultScriptDialogsEnabled = false/);
assert.doesNotMatch(popup, /child\.CoreWebView2\?\.Navigate\(.*requested|args\.Uri.*Navigate/);
assert.match(price, /_armedSitePriceEditNumber = "";/);
console.log('PASS real same-profile first-party popup with opener retained, external navigation blocked and no auto approval');
