# Reprise for YouTube Music

This Manifest V3 extension connects a YouTube Music tab to Reprise over a
loopback-only WebSocket. No remote backend, account, or API key is used.

## 설치

1. Reprise를 실행합니다.
2. 압축 파일을 풀고 `BrowserExtension` 폴더를 옮기지 않을 위치에 둡니다.
3. Chrome은 `chrome://extensions`, Edge는 `edge://extensions`, Brave는
   `brave://extensions`를 엽니다.
4. **개발자 모드**를 켭니다.
5. **압축해제된 확장 프로그램을 로드합니다**를 누르고 이
   `BrowserExtension` 폴더를 선택합니다.
6. `https://music.youtube.com/`을 열거나 이미 열린 탭을 새로고침합니다.

이 확장은 `music.youtube.com`에만 접근합니다. 고정된 manifest 키로 확장
ID를 유지하므로 Reprise는 다른 WebSocket 출처를 거부할 수 있습니다.
