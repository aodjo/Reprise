<div align="center">

# Reprise

**macOS, Windows, Linux를 위한 가벼운 음악 컨트롤러.**

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](../LICENSE)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-lightgrey.svg)](#요구-사항)
[![Release](https://img.shields.io/github/v/release/aodjo/reprise)](https://github.com/aodjo/reprise/releases)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/aodjo/reprise)](https://github.com/aodjo/reprise/commits)
[![GitHub Issues](https://img.shields.io/github/issues/aodjo/reprise)](https://github.com/aodjo/reprise/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/aodjo/reprise)](https://github.com/aodjo/reprise/pulls)

[English](../README.md) | **한국어**

</div>

---

Reprise를 사용하면 지금 듣고 있는 음악을 클릭 한 번으로 제어할 수 있습니다.
앨범 아트, 곡 제목과 아티스트, 탐색 가능한 재생 막대와 재생 제어 기능을 메뉴
막대 또는 시스템 트레이 패널 하나에 담았습니다. 동기화 가사가 있으면 플레이어와
메뉴 막대, 작업 표시줄 또는 데스크톱 패널에 표시됩니다.

macOS에서는 로컬 앱과 브라우저 확장으로 **Apple Music**, **Spotify**,
**YouTube Music**에 연결합니다. Windows에서는 **Spotify**, **멜론** 등을 시스템
미디어 세션으로, Linux에서는 **MPRIS를 지원하는 플레이어**를 연결합니다.
로컬 재생 제어에는 계정이나 API 키가 필요하지 않습니다. 원하면 Google 계정을
연결해 [reprise.mx](https://reprise.mx)에서 청취 기록, 차트와 프로필을 이용할 수 있습니다.

## 주요 기능

- **여러 플레이어를 한곳에서 제어** — 각 운영체제의 로컬 미디어 연동으로 현재 재생 중인 플레이어를 자동으로 따라갑니다
- **한눈에 보는 재생 정보** — 메뉴 막대 또는 트레이 패널에서 앨범 아트, 곡 제목과 아티스트를 확인할 수 있습니다
- **완전한 재생 제어** — 사용 중인 앱을 벗어나지 않고 재생, 일시 정지, 이전·다음 곡 이동과 탐색을 할 수 있습니다
- **재생 막대** — 현재 위치를 확인하고 원하는 지점으로 바로 이동할 수 있습니다
- **동기화 가사** — 플레이어와 메뉴 막대, Windows 작업 표시줄 또는 지원되는 Linux 패널에서 가사를 표시합니다
- **내 데스크톱에 맞춘 구성** — 제목 형식, 앨범 아트 표시 방식, 캐러셀 동작, 가사 영역 너비와 패널 테마를 설정할 수 있습니다
- **운영체제에 맞는 앱** — macOS에서는 SwiftUI, Windows와 Linux에서는 공통 Avalonia 플레이어를 사용합니다
- **로컬 플레이어 제어** — 재생 상태와 제어 명령은 Reprise와 로컬 앱 또는 브라우저 확장 사이에서만 오갑니다
- **선택형 청취 기록 공유** — Google 로그인과 연결 동의 후 reprise.mx에서 프로필과 청취 차트를 이용할 수 있습니다

## 요구 사항

| 운영체제 | 요구 사항 | 플레이어 연결 |
| --- | --- | --- |
| macOS | macOS 14 Sonoma 이상 | 음악 앱, Spotify 또는 Reprise 브라우저 확장을 사용하는 YouTube Music |
| Windows | Windows 10 1809 이상, x64 또는 ARM64 | Windows 미디어 세션을 제공하는 앱과 브라우저 |
| Linux | D-Bus 세션 버스가 있는 데스크톱, x64 또는 ARM64 | MPRIS를 지원하는 앱과 브라우저 |

처음 실행하면 macOS에서 음악 앱 제어 권한을 요청합니다. Reprise가 현재 곡
정보를 읽고 재생 명령을 보내기 위해 필요한 권한입니다. 이 권한은 언제든지
**시스템 설정 → 개인정보 보호 및 보안 → 자동화**에서 확인할 수 있습니다.

## 설치

### macOS — Homebrew

```bash
brew install --cask aodjo/tap/reprise
```

### macOS — Mac App Store

출시 예정입니다.

### macOS — 직접 다운로드

[Releases](https://github.com/aodjo/reprise/releases) 페이지에서 최신 `.dmg`를
다운로드하세요.

### macOS — 소스에서 빌드

```bash
git clone https://github.com/aodjo/reprise.git
cd reprise
open src/Reprise.Platform.MacOS/Reprise.xcodeproj
```

Xcode 16 이상에서 `Reprise` 스킴을 빌드하고 실행하세요.

### Windows

[Windows 다운로드](https://aodjo.github.io/Reprise/windows/)에서 Intel/AMD PC는
**x64**, Windows on Arm은 **ARM64** 설치 파일을 선택하세요. Windows 10 버전
1809 이상이 필요하며, .NET이 포함되어 별도 설치나 관리자 권한이 필요하지 않습니다.

트레이 아이콘으로 플레이어와 설정을 엽니다. **설정 → 시스템 정보 → 업데이트 확인**에서
같은 릴리스 단계와 CPU에 맞는 다음 빌드를 설치할 수 있습니다. 현재 개발 설치 파일은
코드 서명되지 않았습니다. Windows 미디어 세션을 제공하는 앱·브라우저와 연결되며,
로컬에서 재생 중인 Spotify와 멜론은 플레이어별 음량 제어도 지원합니다.

소스 빌드 방법은 [Windows 패키징](../packaging/windows/README.md)을 참고하세요.

### Linux

Avalonia 플레이어가 D-Bus 세션 버스를 통해 MPRIS 재생 정보와 제어 명령을 주고받습니다.
Debian·Ubuntu 계열에서는 apt 저장소를 추가해 설치할 수 있습니다.

```bash
echo "deb [trusted=yes] https://aodjo.github.io/Reprise release main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update && sudo apt install reprise
```

[Linux 다운로드](https://aodjo.github.io/Reprise/)에서 AppImage도 받을 수 있습니다.
GNOME에서는 패키지에 포함된 셸 확장이 패널의 곡 정보와 가사를 표시합니다.
소스에서 실행하려면 .NET SDK를 설치한 뒤 다음 명령을 사용하세요.

```bash
dotnet run --project src/Reprise.Platform.Linux/Reprise.Platform.Linux.csproj
```

패키지와 업데이트 채널은 [Linux 패키징](../packaging/linux/README.md)을 참고하세요.

## 사용 방법

메뉴 막대 또는 시스템 트레이의 Reprise 아이콘을 클릭하면 플레이어 패널이 열립니다.

Reprise는 재생 중인 플레이어를 우선해서 표시합니다. 둘 이상의 플레이어가
동시에 재생 중이면 **설정 → 일반 → 표시 우선순위**에 지정된 순서를 따릅니다.
현재 재생 중인 플레이어가 없다면 곡이 남아 있는 플레이어 중 우선순위가 가장
높은 항목을 계속 표시합니다. 설정의 항목을 드래그하면 순서를 바꿀 수 있습니다.

플레이어 패널의 톱니바퀴 버튼을 누르면 설정을 열 수 있습니다. 패널이 열린
상태에서는 macOS의 <kbd>⌘</kbd><kbd>,</kbd> 단축키도 사용할 수 있습니다.
운영체제에 따라 다음 항목을 설정할 수 있습니다.

- 새 플레이어가 재생을 시작할 때 기존 플레이어 자동 일시 정지
- 플레이어 표시 우선순위와 YouTube Music 확장 연결 상태
- 메뉴 막대·작업 표시줄·Linux 패널의 동기화 가사, 가사 영역 너비와 공간 확보
- 화이트, 다크, Liquid, 시스템 설정 연동 패널 테마
- 제목 표시 형식, 앨범 아트·CD·레벨 인디케이터 표시 방식과 캐러셀 속도
- 현재 재생 시간, 남은 시간과 전체 길이 표시 방식

macOS에서 YouTube Music을 사용하려면 별도의 브라우저 확장이 필요합니다. 로컬 설치
방법은 [브라우저 확장 안내](../BrowserExtension/README.md)를 참고하세요.
Windows와 Linux에서는 브라우저가 제공하는 시스템 미디어 세션 또는 MPRIS로
재생 정보를 가져올 수 있습니다.

청취 기록을 공유하려면 설정의 **Reprise.mx** 항목에서 Google 계정을 연결하세요.
브라우저에서 연결에 동의한 뒤부터 공유가 시작되며, 설정에서 언제든 연결을 해제할 수 있습니다.

## 기여

버그 제보, 기능 제안, Pull Request 등 모든 형태의 기여를 환영합니다.

Reprise는 GPLv3로 공개되며, Mac App Store에서는 별도의 라이선스 조건으로도
배포됩니다. 두 방식의 배포를 가능하게 하기 위해 기여자는 Contributor License
Agreement(CLA)에 동의해야 합니다. 기여한 작업의 저작권은 기여자가 그대로
보유하며, CLA는 해당 기여물을 두 라이선스 조건으로 배포할 권한을 부여합니다.

서명 절차는 자동으로 진행됩니다. Pull Request를 열면 CLA 봇이 서명 링크가
포함된 댓글을 남깁니다. 최초 한 번만 댓글로 동의하면 이후에는 다시 서명할
필요가 없습니다. 문서 수정과 오타 수정만 포함된 Pull Request는 예외입니다.

자세한 내용은 [CONTRIBUTING.md](../CONTRIBUTING.md)를 참고하세요.

## 라이선스

Reprise는 [GNU General Public License v3.0](../LICENSE)에 따라 배포되는 자유
소프트웨어입니다.

누구나 Reprise를 사용하고, 연구하고, 수정하고, 공유할 수 있습니다. 수정한
버전을 배포할 경우 소스 코드와 함께 GPLv3로 공개해야 합니다.

Mac App Store에서 배포되는 버전에는 저작권자의 허가에 따라 별도의 독점
라이선스 조건이 적용됩니다.

**“Reprise”라는 이름과 Reprise 아이콘은 Junsung Lee의 상표이며 GPL의 적용
대상이 아닙니다. 포크에서는 다른 이름과 아이콘을 사용해야 합니다.**

## 고지

Reprise는 독립 프로젝트이며 Apple Inc. 또는 Spotify AB와 제휴하거나
이들로부터 보증 또는 후원을 받지 않습니다.

Apple과 Apple Music은 미국 및 기타 국가에 등록된 Apple Inc.의 상표입니다.
Spotify는 Spotify AB의 상표이며, YouTube와 YouTube Music은 Google LLC의
상표입니다.

---

<div align="center">
<sub>Copyright © 2026 <a href="https://junx.dev/">Junsung Lee</a>. 모든 권리 보유.</sub>
</div>
