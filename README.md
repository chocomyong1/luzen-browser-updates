# Luzen Browser 업데이트 배포

이 저장소는 **Luzen Browser Windows 프로그램의 자동 업데이트 배포용 공개 저장소**입니다.

- 공개 대상: Windows용 빌드 파일 및 버전 안내 파일(`latest.json`)
- 비공개 대상: 프로그램 소스 코드, 계정·비밀번호·쿠키, 사용자 물품 목록 및 설정
- 설치 및 실행: 사용자 PC에 설치된 Luzen Browser가 새 버전을 확인하고, SHA-256을 검사한 후 업데이트합니다.

## 자동 업데이트 파일 형식

`latest.json`은 실제 Windows 실행 파일의 배포가 준비된 뒤에 게시합니다. 업데이트 ZIP은 루트에 `Published/LuzenBrowserReplica.exe` 및 `Published/Assets/`를 포함해야 합니다.

```json
{
  "schema": 1,
  "version": "23.0.0",
  "packageUrl": "https://<실제 파일 다운로드 주소>",
  "sha256": "<실제 ZIP 파일의 SHA-256>",
  "format": "binary",
  "releaseNotes": "변경 내용"
}
```

위 JSON은 **형식 설명용 예시**이며 실제 최신 버전을 알리는 파일이 아닙니다.

## 주의

개발용 소스 ZIP이나 개인 설정 파일을 공개 배포하지 않습니다. Windows 빌드 검증과 배포 파일 준비가 완료되기 전에는 업데이트를 공개하지 않습니다.
