const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const models = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationModels.cs'), 'utf8');
const grids = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.ModernTables.cs'), 'utf8');

assert.match(models, /public string StatusDisplay/);
for (const text of ['확인 필요', '실행 중', '제출 확인 중', '오류', '자동 꺼짐', '완료', '예약 필요', '실행 대기', '예약됨'])
    assert.ok(models.includes(`"${text}"`), `Missing display status ${text}`);
assert.match(grids, /if \(field == "LastStatus"\)\s*\{\s*e\.Value = job\.StatusDisplay/);
assert.match(grids, /e\.ToolTipText = job\.LastStatus/);
assert.match(models, /public string LastStatus \{ get; set; \}/);
console.log('Status grid shows normalized state and preserves diagnostic tooltip');
