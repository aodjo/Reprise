# Reprise for YouTube Music

이 Manifest V3 확장은 YouTube Music 탭과 Reprise를 로컬 전용 WebSocket으로
연결합니다. 원격 서버, 별도 계정, API 키를 사용하지 않습니다.

## Chromium

1. Reprise를 실행합니다.
2. `Chromium` 폴더를 옮기지 않을 위치에 둡니다.
3. Chrome은 `chrome://extensions`, Edge는 `edge://extensions`, Brave는
   `brave://extensions`를 엽니다.
4. **개발자 모드**를 켭니다.
5. **압축해제된 확장 프로그램을 로드합니다**를 누르고 이
   `Chromium` 폴더를 선택합니다.
6. `https://music.youtube.com/`을 열거나 이미 열린 탭을 새로고침합니다.

## Mozilla Firefox

1. Reprise를 실행합니다.
2. Firefox에서 `about:debugging#/runtime/this-firefox`를 엽니다.
3. **임시 부가 기능 로드**를 누릅니다.
4. `Mozilla/manifest.json`을 선택합니다.
5. `https://music.youtube.com/`을 열거나 이미 열린 탭을 새로고침합니다.

임시 부가 기능은 Firefox를 재시작하면 제거됩니다. 지속 설치용 배포본은
Mozilla 서명을 받은 XPI로 제공해야 합니다.

두 확장 모두 `music.youtube.com`에만 접근합니다. Chromium은 고정 manifest
키, Firefox는 고정 Add-on ID를 사용하며 Reprise와의 통신은 로컬 컴퓨터
안에서만 처리됩니다.
