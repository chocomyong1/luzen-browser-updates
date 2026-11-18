'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const main = fs.readFileSync(path.join(__dirname, '../AppSource/MainForm.cs'), 'utf8');
const automation = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const index = automation.indexOf('private void BuildAutoRegistrationTab()');
const end = automation.indexOf('private void ConfigureAutoGrid()', index);
assert(index >= 0 && end > index, '등록 현황 화면을 찾지 못함');
const autoTab = automation.slice(index, end);

// The host and child form must expose ONE navigation layer and ONE run-control set.
assert(!main.includes('BuildModeTabs()'), '메인 중복 메뉴 탭이 다시 추가됨');
for (const [caption, handler] of [
  ['자동 시작', 'StartAutomationFromHeader()'],
  ['일시정지', 'PauseAutomationFromHeader()'],
  ['즉시 중지', 'StopAutomationFromHeader()']
]) {
  assert(main.includes(`AddToolButton(bar, "${caption}", () => _module.${handler}`),
    `${caption} 상단 단일 실행 제어가 사라짐`);
}
assert(!automation.includes('private Control BuildToolbar()'), '안쪽에 중복 실행 도구막대가 있음');
assert(automation.includes('RowCount = 1') &&
  !automation.includes('outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46))'),
  '불필요한 두 번째 상단 줄이 다시 생김');
assert(main.includes('systemMenu.Items.Add("A 기본"') &&
  main.includes('systemMenu.Items.Add("B VPN"') && main.includes('systemMenu.Items.Add("C VPN"'),
  '시스템 전환 드롭다운이 사라짐');
assert(main.includes('moreMenu.Items.Add("내 등록글 불러오기"') &&
  main.includes('_module.ImportMyRegisteredListingsFromHeaderAsync()') &&
  !main.includes('AddToolButton(bar, "내 등록글 불러오기"'),
  '등록글 불러오기는 상단 관리 드롭다운에만 있어야 함');

// The monitoring tab is only a status table. Editors and actions have one home.
assert(autoTab.includes('_autoTab.Controls.Add(_autoGrid)') &&
  autoTab.includes('EditAutoSelectedJob()') &&
  !autoTab.includes('MakeButton(') && !autoTab.includes('_autoTab.Controls.Add(buttons)'),
  '등록 현황은 버튼 줄 없이 표와 더블클릭 편집 이동만 제공해야 함');
assert.match(automation, /private Control BuildSegmentedNavigation\(\)/,
  '윈도우 기본 탭 대신 사용자 지정 세그먼트 메뉴가 있어야 함');
const navigation = automation.slice(automation.indexOf('private Control BuildSegmentedNavigation()'), index);
assert(navigation.includes('var pages = new[] { _autoTab, _itemTab }') &&
  !navigation.includes('"고급 설정"'),
  '고급 설정은 일반 탭에 표시하지 않아야 함');
assert.match(automation, /_editorTabs\.Location = new Point\(-4, -hiddenNativeHeader\)/,
  '기본 WinForms 탭 헤더가 노출되면 안 됨');
assert.match(automation, /_log\.Visible = false/,
  '로그 영역은 기본 접힘 상태여야 함');
assert.match(main, /TextRenderer\.MeasureText\(label, button\.Font\)/,
  '상단 실행 버튼의 글자가 잘리면 안 됨');
const itemToolbar = automation.slice(automation.indexOf('private void BuildJobTab()'), automation.indexOf('private void ConfigureJobsGrid()', automation.indexOf('private void BuildJobTab()')));
for (const caption of ['+ 새 물품', '같은 가격 선택', '일괄수정', '가격 반영 예약', '더보기 ▾'])
  assert(itemToolbar.includes(`ActionButton("${caption}"`), `물품 편집의 ${caption} 버튼이 사라짐`);
assert(itemToolbar.includes('selectionTools.Items.Add("CSV 가져오기"') &&
  itemToolbar.includes('selectionTools.Items.Add("표 넓게 보기/복원"'),
  '보조 도구 메뉴에서 CSV 또는 표 넓게 보기가 사라짐');
assert(itemToolbar.includes('SetBatchEnabled(true)') && itemToolbar.includes('SetBatchEnabled(false)'),
  '편집 탭 일괄 자동 ON/OFF 경로가 사라짐');
assert(main.includes('"판매글 추가"') && main.includes('AddNewListing("판매등록")'),
  '판매글 신규 등록 메뉴가 사라짐');
assert(main.includes('"구매글 추가"') && main.includes('AddNewListing("구매등록")'),
  '구매글 신규 등록 메뉴가 사라짐');
assert(main.includes('"재등록 추가"') && main.includes('AddNewListing("재등록")'),
  '재등록 신규 등록 메뉴가 사라짐');
for (const pathName of ['OpenSystemNetworkSettings("B")', 'OpenSystemNetworkSettings("C")', 'OpenUpdateSettingsDialog()',
  '_module.ShowWorkflowSettings()', '_module.OpenAccountSettings()']) {
  assert(main.includes(pathName), `${pathName} 메뉴 이동 경로가 사라짐`);
}
assert(!autoTab.includes('new Label\n        {\n            Text = "1. 기준 물품 선택'),
  '화면을 차지하는 긴 안내문이 다시 추가됨');
assert(!autoTab.includes('_autoTab.Controls.Add(help)'), '작업 안내 줄이 다시 생김');
assert(!autoTab.includes('MakeButton("설정 저장"'),
  '안쪽 목록의 중복 저장 버튼이 남아 있음');

// Narrow right-hand editors and high-DPI Windows font scaling must not
// truncate action labels or hide later wrapped lines.
assert.match(automation, /TextRenderer\.MeasureText\(text, b\.Font\)/,
  '상세 편집 버튼 글자 폭 자동 계산이 사라짐');
assert.match(automation, /bottom\.SizeChanged \+=.*FitBottomHeight\(\)/,
  '상세 편집 버튼 줄바꿈 높이 조절이 사라짐');
assert.match(automation, /AutoScroll = false, WrapContents = true/,
  '상세 편집 버튼이 고정 높이에서 잘릴 위험');

console.log('PASS: monitoring-only status page, editing actions grouped and preserved routes');
