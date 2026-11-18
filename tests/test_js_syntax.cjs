// Syntax-check each important JavaScript snippet embedded in the C# app.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '..', 'AppSource');
function section(filename, marker) {
  const source = fs.readFileSync(path.join(root, filename), 'utf8');
  const start = source.indexOf(marker);
  if (start < 0) throw new Error(`missing method/field: ${marker}`);
  const open = source.indexOf('"""', start);
  const end = source.indexOf('"""', open + 3);
  if (open < 0 || end < 0) throw new Error(`missing raw string terminator: ${marker}`);
  return source.slice(open + 3, end).replaceAll('__ARG__', '{}').replaceAll('__KEY__', '"Title"')
    .replaceAll('__EXPECTED_NUMBER__', '"2026070507571234"');
}
const snippets = [
  ['OwnedListRepost.cs', 'public const string BrowserScript'],
  ['ListingIdentity.cs', 'public const string BrowserScript'],
  ['MyListingsImport.cs', 'public const string ExtractPageScript'],
  ['MyListingsImport.cs', 'public const string ExtractDetailScript'],
  ['AutomationForm.cs', 'private async Task<string> ResolveAutoSelectorAsync'],
  ['AutomationForm.cs', 'private async Task ExecuteStepAsync'],
  ['AutomationForm.cs', 'private async Task ConfirmExactRegistrationDialogAsync'],
  ['AutomationForm.SiteDiagnostics.cs', 'private async Task ExportSiteDiagnosticsAsync'],
  ['SitePriceEdit.cs', 'public const string OwnedRowScript'],
  ['SitePriceEdit.cs', 'public const string EditFormScript']
];
for (const [file, marker] of snippets) {
  const code = section(file, marker);
  new vm.Script(code, { filename: `${file}: ${marker}` });
  console.log(`PASS JavaScript syntax: ${file} / ${marker.split(' ').at(-1)}`);
}
