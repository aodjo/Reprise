; Per-user, stage- and architecture-specific Windows installer.
Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 128
CRCCheck force
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

!define APP_KEY "Software\Reprise\${CHANNEL}\${ARCH}"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Reprise-${CHANNEL}-${ARCH}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "Reprise (${CHANNEL}, ${ARCH})"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Reprise-${CHANNEL}-${ARCH}"
InstallDirRegKey HKCU "${APP_KEY}" "InstallLocation"
VIProductVersion "${FILE_VERSION}"
VIAddVersionKey "ProductName" "Reprise ${CHANNEL}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "FileDescription" "Reprise ${CHANNEL} installer (${ARCH})"
VIAddVersionKey "LegalCopyright" "Copyright aodjo"

!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\Reprise.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Korean"
!insertmacro MUI_LANGUAGE "English"

; Refuse to replace an active stage without forcibly terminating user processes.
!macro CHECK_CLOSED PREFIX
Function ${PREFIX}CheckClosed
  retry:
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\dev.junx.Reprise.${CHANNEL}") p .r0'
  IntCmp $0 0 done
  System::Call 'kernel32::CloseHandle(p r0)'
  IfSilent blocked
  MessageBox MB_RETRYCANCEL|MB_ICONINFORMATION "Reprise를 종료한 뒤 다시 시도해 주세요." IDRETRY retry
  blocked:
  SetErrorLevel 2
  Quit
  done:
FunctionEnd
!macroend
!insertmacro CHECK_CLOSED ""
!insertmacro CHECK_CLOSED "un."

; Match the registry view used by the application process.
Function .onInit
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Windows 10 이상이 필요합니다."
    SetErrorLevel 3
    Quit
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 17763
    MessageBox MB_OK|MB_ICONSTOP "Windows 10 버전 1809 이상이 필요합니다."
    SetErrorLevel 3
    Quit
  ${EndIf}
  !if "${ARCH}" == "arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_OK|MB_ICONSTOP "이 설치 파일은 Windows ARM64용입니다."
      SetErrorLevel 3
      Quit
    ${EndIf}
    SetRegView 64
  !else if "${ARCH}" == "x64"
    ${IfNot} ${RunningX64}
      MessageBox MB_OK|MB_ICONSTOP "이 설치 파일은 64비트 Windows용입니다."
      SetErrorLevel 3
      Quit
    ${EndIf}
    SetRegView 64
  !else
    SetRegView 32
  !endif
  SetShellVarContext current
  Call CheckClosed
FunctionEnd

; Preserve preferences and other installed stages when removing this application.
Function un.onInit
  !if "${ARCH}" == "x86"
    SetRegView 32
  !else
    SetRegView 64
  !endif
  SetShellVarContext current
  Call un.CheckClosed
FunctionEnd

Section "Reprise"
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ClearErrors
  File /r "${APP_DIR}/*"
  File /oname=LICENSE.txt "${LICENSE_FILE}"
  ${If} ${Errors}
    SetErrorLevel 4
    Abort "프로그램 파일을 설치하지 못했습니다."
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${APP_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APP_KEY}" "Version" "${APP_VERSION}"
  WriteRegStr HKCU "${APP_KEY}" "BuildNumber" "${BUILD_NUMBER}"
  WriteRegStr HKCU "${APP_KEY}" "Channel" "${CHANNEL}"
  WriteRegStr HKCU "${APP_KEY}" "Architecture" "${ARCH}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Reprise (${CHANNEL}, ${ARCH})"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "aodjo"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Reprise.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/aodjo/Reprise"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  CreateShortcut "$SMPROGRAMS\Reprise (${CHANNEL}, ${ARCH}).lnk" "$INSTDIR\Reprise.exe"
SectionEnd

Section "Uninstall"
  ReadRegStr $0 HKCU "${RUN_KEY}" "Reprise-${CHANNEL}"
  StrCmp $0 '$\"$INSTDIR\Reprise.exe$\" --startup' 0 +2
    DeleteRegValue HKCU "${RUN_KEY}" "Reprise-${CHANNEL}"
  Delete "$SMPROGRAMS\Reprise (${CHANNEL}, ${ARCH}).lnk"
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\Uninstall.exe"
  DeleteRegKey HKCU "${APP_KEY}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  RMDir "$INSTDIR"
SectionEnd
