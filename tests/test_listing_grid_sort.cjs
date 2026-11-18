'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../AppSource', name), 'utf8');
const sorting = read('AutomationForm.GridSort.cs');
const app = read('AutomationForm.cs');

assert.match(app, /InstallListingHeaderSorting\(\)/);
assert.match(app, /_filteredJobs = SortFilteredListingJobs\(_jobs\.Where\(MatchesFilter\)\)/);
assert.match(app, /RefreshListingSortGlyphs\(\)/);
assert.match(sorting, /foreach \(var grid in new\[\] \{ _autoGrid, _jobsGrid \}\)/,
  'Both views must share the same ordering');
assert.match(sorting, /column\.DataPropertyName is "Server" or "Price"/);
assert.match(sorting, /grid\.ColumnHeaderMouseClick \+=/);
assert.match(sorting, /column\.SortMode = DataGridViewColumnSortMode\.Programmatic/);
assert.match(sorting, /_listingSortAscending = _listingSortField == field \? !_listingSortAscending : true/);
assert.match(sorting, /OrderBy\(job => job\.Server/, 'Server must use text order');
assert.match(sorting, /OrderByDescending\(job => job\.Server/, 'Server must reverse on second click');
assert.match(sorting, /TrySitePrice\(job\.Price, out var value\)/,
  'Price must sort as number, not a comma-formatted string');
assert.match(sorting, /ThenBy\(PriceOf\)/);
assert.match(sorting, /ThenByDescending\(PriceOf\)/);
assert.match(sorting, /RefreshFilteredLists\(focused\?\.Id\)/,
  'Sorting should preserve the focused listing');
assert.match(sorting, /SortGlyphDirection/);
assert.doesNotMatch(sorting, /\bgrid\.Sort\(|\.SelectedForBulk = false/,
  'Header sorting must not mutate bulk selection or rely on unsupported BindingList sort');
console.log('PASS clickable server and numeric price column headers, both grids and stable selected rows');
