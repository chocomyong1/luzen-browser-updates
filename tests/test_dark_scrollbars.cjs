'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = name => fs.readFileSync(path.join(__dirname, '..', 'AppSource', name), 'utf8');
const theme = source('UiTheme.cs');
const scrollbars = source('DarkScrollbarTheme.cs');
const automation = source('AutomationForm.cs');
const integrated = source('MainForm.IntegratedEditor.cs');
const workflow = fs.readFileSync(path.join(__dirname, '..', '.github', 'workflows', 'build-windows.yml'), 'utf8');

// Native scrollbar theming leaves scroll positions and all input behavior owned
// by the WinForms controls; do not replace or hide their real scrollbars.
assert.match(scrollbars, /SetWindowTheme\(IntPtr hwnd/);
assert.match(scrollbars, /"DarkMode_Explorer"/);
assert.match(scrollbars, /SystemInformation\.HighContrast/);
assert.match(scrollbars, /control\.HandleCreated \+=/);
assert.match(scrollbars, /control\.SystemColorsChanged \+=/);
assert.match(scrollbars, /dataGrid\.ControlAdded \+=/);
assert.match(scrollbars, /child is ScrollBar/);
assert.doesNotMatch(scrollbars, /ScrollBars\s*=\s*ScrollBars\.None/);

// The existing A/B/C controls are reached through shared form styling.
assert.match(automation, /UiTheme\.ApplyControlStyles\(this\)/);
assert.match(automation, /RichTextBox _log/);
assert.match(automation, /DataGridView _jobsGrid/);
assert.match(automation, /DataGridView _autoGrid/);
assert.match(theme, /child is DataGridView or RichTextBox or ScrollBar/);
assert.match(theme, /child is ScrollableControl \{ AutoScroll: true \}/);
assert.match(theme, /DarkScrollbarTheme\.Attach\(child\)/);

// The integrated editor grid is built dynamically after the main UI.
assert.match(integrated, /DarkScrollbarTheme\.Attach\(_integratedGrid\)/);
assert.match(workflow, /node tests\/test_dark_scrollbars\.cjs/);
console.log('PASS VANTA native dark scrollbar attachment and lifecycle guards');
