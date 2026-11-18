'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const app = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const main = fs.readFileSync(path.join(__dirname, '../AppSource/MainForm.cs'), 'utf8');
const telegram = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.HourlyQuota.cs'), 'utf8');
const models = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationModels.cs'), 'utf8');
const start = main.indexOf('private Control BuildToolbar()');
const finish = main.indexOf('private void AddNewListing(', start);
assert(start !== -1 && finish > start, '실행 도구막대를 찾지 못함');
const toolbar = main.slice(start, finish);

assert(app.includes('RowCount = 1') && !app.includes('outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46))'),
  '불필요한 실행 줄이 남아 있음');
assert(toolbar.includes('"시간당 등록 횟수 설정"') && app.includes('public void OpenHourlyLimitSettings()'),
  '시간당 설정의 관리 메뉴 경로가 사라짐');
for (const caption of ['자동 시작', '일시정지', '즉시 중지'])
  assert(toolbar.includes(`AddToolButton(bar, "${caption}"`), `${caption} 버튼이 통합 상단에서 사라짐`);
for (const oldInput of ['_serverLimit', '_serverDelay', '_host']) {
  assert(!toolbar.includes(oldInput), `숨겨야 할 입력칸 ${oldInput}이 남아 있음`);
  assert(!app.includes(`private readonly NumericUpDown ${oldInput}`) &&
    !app.includes(`private readonly TextBox ${oldInput}`), `${oldInput}의 편집 경로가 남음`);
}
assert(app.includes('_config.MaxListingsPerServer = (int)_hourLimit.Value;'),
  '서버 물품 상한이 시간당 설정값과 연동되지 않음');
assert(app.includes('_config.MinimumServerSeconds = 21;'),
  '동일 서버 21초 제한이 고정되지 않음');
assert(app.includes('_config.AllowedHost = "www.itemmania.com";'),
  '허용 사이트가 공식 도메인으로 고정되지 않음');
assert(models.includes('Math.Clamp(loaded.MaxListingsPerServer, 1, 1000)'),
  '서버 물품 수 설정의 상한이 시간당 설정 범위보다 작음');
assert(main.includes('"텔레그램 알림 설정"') &&
  main.includes('_module.OpenTelegramSettings()') &&
  telegram.includes('public void OpenTelegramSettings()'),
  '텔레그램 설정 메뉴 접근 경로가 사라짐');
console.log('PASS compact scheduler controls, synchronized caps, fixed host/pacing and Telegram access');
