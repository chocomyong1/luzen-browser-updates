'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const main = fs.readFileSync(path.join(__dirname, '../AppSource/MainForm.cs'), 'utf8');
const automation = fs.readFileSync(path.join(__dirname, '../AppSource/AutomationForm.cs'), 'utf8');
const index = automation.indexOf('private void BuildAutoRegistrationTab()');
const end = automation.indexOf('private void ConfigureAutoGrid()', index);
assert(index >= 0 && end > index, '자동등록 목록 도구막대를 찾지 못함');
const autoTab = automation.slice(index, end);

// The host and child form must expose ONE navigation layer and ONE run-control set.
assert(!main.includes('BuildModeTabs()'), '메인 중복 메뉴 탭이 다시 추가됨');
for (const [caption, handler] of [
  ['자동 시작', 'StartScheduler()'],
  ['일시정지', 'PauseScheduler()'],
  ['즉시 중지', 'StopScheduler()']
]) {
  assert(automation.includes(`Btn("${caption}", (_, _) => ${handler}`),
    `${caption} 상단 단일 실행 제어가 사라짐`);
}
assert(!/MakeButton\("(?:자동등록 시작|일시정지|중지)"/.test(autoTab),
  '안쪽 목록 도구막대에 중복 실행 제어가 있음');

// Existing features are not deleted: only their presentation is regrouped.
for (const caption of ['선택 물품 편집', '+ 물품 추가', '같은 가격 선택',
  '선택 물품 일괄수정', '선택 가격 반영', '선택/설정 ▾']) {
  assert(autoTab.includes(`MakeButton("${caption}"`), `${caption} UI가 사라짐`);
}
for (const action of ['조회 전체선택', '선택 해제', '선택 물품 자동 ON', '선택 물품 자동 OFF'])
  assert(autoTab.includes(`batchMenu.Items.Add("${action}"`), `${action} 메뉴가 사라짐`);
assert(!autoTab.includes('MakeButton("조회 전체선택"') && !autoTab.includes('MakeButton("선택 해제"'),
  '조회/선택 관리 버튼은 보조 메뉴에서만 보여야 함');
const itemToolbar = automation.slice(automation.indexOf('private void BuildJobTab()'), automation.indexOf('private void ConfigureJobsGrid()', automation.indexOf('private void BuildJobTab()')));
assert(itemToolbar.includes('ActionButton("같은 가격 선택"') && itemToolbar.includes('ActionButton("선택 가격 반영"'),
  '편집 탭 일괄 가격 동선이 사라짐');
assert(itemToolbar.includes('selectionTools.Items.Add("CSV 가져오기"') &&
  itemToolbar.includes('selectionTools.Items.Add("표 넓게 보기/복원"'),
  '보조 도구 메뉴에서 CSV 또는 표 넓게 보기가 사라짐');
assert(autoTab.includes('SetBatchEnabled(true)') && autoTab.includes('SetBatchEnabled(false)'),
  '일괄 자동 ON/OFF 경로가 사라짐');
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
assert(automation.includes('Btn("내 등록글 불러오기"'),
  '기존 등록글 조회 상단 제어가 사라짐');
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

console.log('PASS: navigation, single run control, grouped batch actions and preserved routes');
