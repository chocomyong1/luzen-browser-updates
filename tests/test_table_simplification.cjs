const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const form = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const modern = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.ModernTables.cs'), 'utf8');
const models = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationModels.cs'), 'utf8');

// Production UI has one clear Start/Pause/Stop path: no test/manual-run
// buttons or redundant live-posting checkbox/confirmation gate.
const toolbar = form.slice(form.indexOf('private Control BuildToolbar()'), form.indexOf('private static void AddInline('));
const startScheduler = form.slice(form.indexOf('private void StartScheduler()'), form.indexOf('private void PauseScheduler()'));
assert.doesNotMatch(toolbar, /1건 실제 실행|시험 실행|실제 제출 허용/);
assert.doesNotMatch(form, /_livePosting/);
assert.doesNotMatch(startScheduler, /MessageBoxButtons\.YesNo/);
assert.match(toolbar, /Btn\("자동 시작"/);
assert.match(toolbar, /Btn\("일시정지"/);
assert.match(toolbar, /Btn\("즉시 중지"/);

const listColumns = form.slice(form.indexOf('private void ConfigureJobsGrid()'), form.indexOf('private void ConfigureStepsGrid()'));
assert.match(listColumns, /HeaderText = "수량", DataPropertyName = "QuantityDisplay"/);
for (const label of ['단위', '서브 ID', '다음 예정', '상태']) {
    assert.doesNotMatch(listColumns, new RegExp(`HeaderText = "${label}"`), `${label} should be hidden from the product list`);
}
assert.match(models, /public string QuantityDisplay/);
assert.match(models, /quantity\.EndsWith\(unit, StringComparison\.Ordinal\)/);
assert.match(models, /quantity : quantity \+ unit/);
assert.match(models, /if \(quantity\.Length == 0\) return ImportedFromSite \? "미수집"/);
assert.match(modern, /ReorderColumns\(_jobsGrid,[\s\S]*?"QuantityDisplay"/);
assert.match(form, /_autoGrid\.Columns\.Add\(new DataGridViewTextBoxColumn/);
console.log('Product list shows quantity with unit, omits redundant tracking columns, preserves auto controls');
