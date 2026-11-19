'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const panel = fs.readFileSync(path.join(__dirname, '..', 'AppSource', 'MainForm.IntegratedEditor.cs'), 'utf8');
const theme = fs.readFileSync(path.join(__dirname, '..', 'AppSource', 'UiTheme.cs'), 'utf8');

const gridStart = panel.indexOf('_integratedGrid = new DataGridView');
const columnsStart = panel.indexOf('_integratedGrid.Columns.Add(', gridStart);
assert.ok(gridStart >= 0 && columnsStart > gridStart, 'Missing integrated data grid');
const grid = panel.slice(gridStart, columnsStart);

assert.match(theme, /Canvas = Color\.FromArgb\(16, 24, 32\)/);
assert.match(theme, /Surface = Color\.FromArgb\(30, 41, 51\)/);
assert.match(theme, /Text = Color\.FromArgb\(241, 245, 249\)/);
assert.match(grid, /EnableHeadersVisualStyles = false/,
  'Windows default white column headers must be disabled');
assert.match(grid, /ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle/);
assert.match(grid, /DefaultCellStyle = new DataGridViewCellStyle/);
assert.match(grid, /AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle/);
assert.match(grid, /BackColor = UiTheme\.Surface, ForeColor = UiTheme\.Text/);
assert.match(grid, /BackColor = UiTheme\.Canvas, ForeColor = UiTheme\.Text/);
assert.match(grid, /SelectionBackColor = Color\.FromArgb\(38, 83, 77\)/);
assert.match(grid, /SelectionForeColor = UiTheme\.Text/);
assert.doesNotMatch(grid, /BackColor = Color\.White/);
const filters = panel.slice(panel.indexOf('_integratedSystemFilter = new ComboBox'), gridStart);
assert.equal((filters.match(/BackColor = UiTheme\.SurfaceAlt, ForeColor = UiTheme\.Text/g) || []).length, 3,
  'System/server dropdowns and search field must match the dark grid');
console.log('PASS VANTA integrated editor grid dark contrast and selection colors');
