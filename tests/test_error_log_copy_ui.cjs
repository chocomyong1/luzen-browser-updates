const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ui = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const helper = fs.readFileSync(path.join(__dirname, '../AppSource/ErrorLogCopy.cs'), 'utf8');

assert.match(ui, /Text = "오류 로그 복사"/);
assert.match(ui, /Clipboard\.SetText\(ErrorLogCopy\.Build\(_log\.Lines,/);
assert.match(ui, /logHeader\.Controls\.Add\(copyErrors, 1, 0\)/);
assert.match(helper, /최근 오류 \{errors\.Length\}건과 관련 작업 기록/);
assert.match(helper, /TelegramToken\.Replace/);
assert.match(helper, /SecretValue\.Replace/);
assert.match(helper, /BearerToken\.Replace/);
assert.doesNotMatch(helper, /https?:\/\//);
console.log('PASS one-click diagnostic log copy, local clipboard only, sensitive values masked');
