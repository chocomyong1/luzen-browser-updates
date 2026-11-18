'use strict';

// Source-level layout regression checks; no Windows display is required.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const theme = app('UiTheme.cs');
const main = app('MainForm.cs');
const form = app('AutomationForm.cs');
const table = app('AutomationForm.ModernTables.cs');

assert.match(theme, /Canvas = Color\.FromArgb\(245, 245, 247\)/);
assert.match(theme, /Primary = Color\.FromArgb\(0, 122, 255\)/);
assert.match(theme, /private static void RoundButton\(Button button\)/);
assert.match(theme, /tabs\.ItemSize = new Size\(153, 40\)/);
assert.match(main, /BackColor = UiTheme\.Surface, Padding = new Padding\(15, 11, 9, 8\)/);
const search = form.slice(form.indexOf('private Control BuildSearchFilters()'),
  form.indexOf('private static string Value(ComboBox combo)'));
assert.match(search, /Height = 51/);
assert.match(search, /advanced = new Panel \{[\s\S]*?Visible = false/);
for (const input of ['_fGame', '_fServer', '_fName'])
  assert.match(search, new RegExp(`Add\\(first, [^\\n]*${input}\\)`));
for (const input of ['_fSub', '_fSite', '_fSiteId', '_fOperation', '_fCategory', '_fPaid'])
  assert.match(search, new RegExp(`Add\\((second|third), [^\\n]*${input}\\)`));
assert.match(search, /search\.Height = expanded \? 139 : 51/);
const editor = form.slice(form.indexOf('private void BuildJobTab()'),
  form.indexOf('private void ConfigureJobsGrid()'));
assert.match(editor, /extraActions\.Items\.Add\("서버별 복제"/);
assert.match(editor, /extraActions\.Items\.Add\("완료 표시 초기화"/);
assert.match(editor, /extraActions\.Items\.Add\("사이트 확인 후 보류 해제"/);
assert.match(editor, /FooterButton\("추가 작업  ▾"/);
assert.match(table, /SelectionBackColor = Color\.FromArgb\(230, 242, 255\)/);
console.log('PASS compact light Apple-inspired layout and preserved controls');
