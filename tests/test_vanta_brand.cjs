'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
const load = name => fs.readFileSync(path.join(root, 'AppSource', name), 'utf8');

const main = load('MainForm.cs');
const settings = load('Settings.cs');
const updates = load('AppUpdateService.cs');
const windowsBoot = load('WindowsAutoStart.cs');
const project = load('LuzenBrowserReplica.csproj');
const workflow = fs.readFileSync(path.join(root, '.github/workflows/build-windows.yml'), 'utf8');

assert.match(main, /Text = "VANTA \| 물품 · 자동등록 관리"/);
assert.match(main, /Text = "VANTA"/);
assert.match(main, /new Icon\(Settings.Asset\("VANTA.ico"\)\)/);
assert.match(project, /<ApplicationIcon>Assets\\VANTA.ico<\/ApplicationIcon>/);
assert.ok(fs.statSync(path.join(root, 'AppSource/Assets/VANTA.ico')).size > 1000,
  'A real multi-resolution Windows icon must be bundled');

// A display-name change must never create new profile folders, new Windows
// startup entries, a second submitter, or an incompatible update channel.
assert.match(settings, /"LuzenBrowserReplica"/);
assert.match(settings, /"SystemB"/);
assert.match(settings, /"SystemC"/);
assert.match(windowsBoot, /EntryName = "LuzenBrowserAutoRegister"/);
assert.match(project, /<AssemblyName>LuzenBrowserReplica<\/AssemblyName>/);
assert.match(updates, /"LuzenBrowserPortable", "ManagedInstall"/);
assert.match(updates, /"https:\/\/raw\.githubusercontent\.com\/chocomyong1\/luzen-browser-updates\/main\/latest\.json"/);
assert.match(updates, /CurrentVersion = "22\.3\.11"/);
assert.match(workflow, /\$version = '22\.3\.11'/);
assert.match(workflow, /Copy-Item -Path 'existing-release\/Published\/Assets\/\*'/);
assert.match(workflow, /vanta-windows-v22\.3\.11/);
console.log('PASS VANTA identity assets and legacy data/update compatibility');
