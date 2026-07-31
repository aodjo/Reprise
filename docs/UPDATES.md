# Reprise 업데이트 배포

Reprise는 Sparkle 2의 서명된 appcast를 사용합니다. 앱은 기본적으로 하루에 한 번 업데이트를 확인하고, 새 버전이 있으면 설치 알림을 표시합니다. 사용자가 설정에서 자동 다운로드를 켤 수도 있습니다.

## 릴리스 순서

1. Xcode에서 `MARKETING_VERSION`과 `CURRENT_PROJECT_VERSION`을 올립니다. Sparkle이 새 빌드를 구분하므로 빌드 번호는 반드시 이전 릴리스보다 커야 합니다.
2. Developer ID로 앱을 Archive하고 notarization까지 마친 뒤 ZIP 또는 DMG로 만듭니다.
3. 업데이트 파일과 같은 이름의 Markdown 릴리스 노트를 한 폴더에 넣습니다. 예: `Reprise 1.2.0.zip`, `Reprise 1.2.0.md`.
4. Sparkle 패키지의 `generate_appcast`를 찾아 실행합니다. 개인 EdDSA 키는 macOS 로그인 키체인의 `ed25519` 항목에 저장되어 있으며 저장소에 넣지 않습니다.

   ```sh
   SPARKLE_GENERATOR=$(find ~/Library/Developer/Xcode/DerivedData -path '*/SourcePackages/artifacts/sparkle/Sparkle/bin/generate_appcast' -print -quit)
   "$SPARKLE_GENERATOR" \
     --download-url-prefix "https://github.com/aodjo/Reprise/releases/download/v1.2.0/" \
     --link "https://github.com/aodjo/Reprise" \
     -o appcast.xml \
     /path/to/update-archives
   ```

5. 생성된 `appcast.xml`을 저장소 루트의 파일과 교체하고, appcast에 기록된 정확한 파일명으로 업데이트 파일을 GitHub Release에 업로드합니다.
6. 이전 배포본에서 `설정 > 시스템 정보 > 업데이트 확인…`을 눌러 다운로드와 설치를 검증한 뒤 appcast를 커밋하고 푸시합니다.

appcast 또는 릴리스 노트를 수동 수정한 경우에는 서명이 달라지므로 `generate_appcast`를 다시 실행해야 합니다. 자세한 내용은 [Sparkle Publishing 문서](https://sparkle-project.org/documentation/publishing/)를 참고하세요.
