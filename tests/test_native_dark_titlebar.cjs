'use strict';

// The native title bar must be themed without replacing the standard window
// frame: resize, maximize and minimize are used during long-running jobs.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '../AppSource');
const main = fs.readFileSync(path.join(root, 'MainForm.cs'), 'utf8');
const theme = fs.readFileSync(path.join(root, 'NativeTitleBarTheme.cs'), 'utf8');

assert.match(main, /FormBorderStyle = FormBorderStyle\.Sizable/);
assert.match(main, /MaximizeBox = true/);
assert.match(main, /MinimizeBox = true/);
assert.match(main, /protected override void OnHandleCreated\(EventArgs e\)[\s\S]*?base\.OnHandleCreated\(e\);[\s\S]*?NativeTitleBarTheme\.Apply\(Handle\);/);
assert.match(theme, /DllImport\("dwmapi\.dll"/);
assert.match(theme, /DwmSetWindowAttribute\(window, DarkMode, ref enabled, sizeof\(int\)\)/);
assert.match(theme, /DwmSetWindowAttribute\(window, LegacyDarkMode, ref enabled, sizeof\(int\)\)/);
assert.match(theme, /OperatingSystem\.IsWindowsVersionAtLeast\(10, 0, 22000\)/);
assert.match(theme, /ColorTranslator\.ToWin32\(UiTheme\.Surface\)/);
assert.match(theme, /ColorTranslator\.ToWin32\(UiTheme\.Text\)/);
assert.match(theme, /DwmSetWindowAttribute\(window, CaptionColor, ref caption, sizeof\(int\)\)/);
assert.match(theme, /DwmSetWindowAttribute\(window, CaptionTextColor, ref foreground, sizeof\(int\)\)/);
// Embedded automation modules intentionally have no border; MAIN remains
// native and sizable despite the child forms using borderless layout.
assert.doesNotMatch(theme, /ReleaseCapture|SendMessage|WM_NCHITTEST|CreateParams|WndProc/);
console.log('PASS dark native Windows title bar without breaking native frame behavior');
