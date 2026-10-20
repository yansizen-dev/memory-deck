Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!define APP "MemoryDeck"
!define VER "1.2.2"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MemoryDeck"
Name "MemoryDeck ${VER}"
OutFile "dist/MemoryDeck-1.2.2-Setup-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\MemoryDeck"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ShowUninstDetails show
BrandingText "MemoryDeck · 本地 AI 协作记忆管理"
Icon "app.ico"
UninstallIcon "app.ico"
VIProductVersion "1.2.2.0"
VIAddVersionKey /LANG=2052 "ProductName" "MemoryDeck"
VIAddVersionKey /LANG=2052 "FileDescription" "MemoryDeck 1.2.2 离线安装包"
VIAddVersionKey /LANG=2052 "FileVersion" "1.2.2.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "MemoryDeck"
!define MUI_ABORTWARNING
!define MUI_ICON "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_WELCOMEPAGE_TITLE "欢迎安装 MemoryDeck"
!define MUI_WELCOMEPAGE_TEXT "管理你的背景、偏好和项目记忆，并导出给 AI 使用。$\r$\n$\r$\n此安装包随附 .NET 10 桌面运行时，无需另外下载运行时。$\r$\n$\r$\n程序仅安装给当前用户。记忆数据单独保存，卸载时会保留。$\r$\n$\r$\n继续前，请先关闭正在运行的 MemoryDeck。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\MemoryDeckLauncher.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动 MemoryDeck"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\使用指南.html"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "查看使用指南"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "此版本需要 Windows 10 / 11（64 位）。"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "此版本需要 Windows 10 或更新版本。"
    Abort
  ${EndIf}
  SetShellVarContext current
  SetRegView 64
FunctionEnd
Section "MemoryDeck 和离线运行时（必选）" SecCore
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "payload/MemoryDeck.exe"
  File "payload/MemoryDeckLauncher.exe"
  File "payload/使用指南.html"
  File "runtime-provenance.json"
  SetOutPath "$INSTDIR\runtime"
  File /r "payload/runtime/*"
  CreateDirectory "$LOCALAPPDATA\MemoryDeck\Data"
  SetOutPath "$LOCALAPPDATA\MemoryDeck\Data"
  SetOverwrite off
  File "payload/templates/MEMORY.md"
  File "payload/templates/AGENTS.md"
  SetOverwrite on
  IfFileExists "$INSTDIR\memorydeck.config.json" config_done
  FileOpen $0 "$INSTDIR\memorydeck.config.json" w
  IfErrors config_error
  FileWrite $0 '{$\r$\n  "DataDirectory": "%LOCALAPPDATA%\\MemoryDeck\\Data",$\r$\n  "MemoryFileNameOverride": "MEMORY.md",$\r$\n  "AgentsFileNameOverride": "AGENTS.md"$\r$\n}$\r$\n'
  FileClose $0
  Goto config_done
config_error:
  MessageBox MB_ICONSTOP "无法写入配置。请检查安装目录的写入权限后重试。"
  Abort
config_done:
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\MemoryDeck"
  CreateShortCut "$SMPROGRAMS\MemoryDeck\MemoryDeck.lnk" "$INSTDIR\MemoryDeckLauncher.exe" "" "$INSTDIR\MemoryDeck.exe" 0
  CreateShortCut "$SMPROGRAMS\MemoryDeck\使用指南.lnk" "$INSTDIR\使用指南.html"
  CreateShortCut "$SMPROGRAMS\MemoryDeck\卸载 MemoryDeck.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "MemoryDeck"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VER}"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\MemoryDeck.exe,0"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${REGKEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
SectionEnd
Section "创建桌面快捷方式" SecDesktop
  CreateShortCut "$DESKTOP\MemoryDeck.lnk" "$INSTDIR\MemoryDeckLauncher.exe" "" "$INSTDIR\MemoryDeck.exe" 0
SectionEnd
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
!insertmacro MUI_DESCRIPTION_TEXT ${SecCore} "安装程序、随附运行时、开始菜单入口及使用指南；保留已有配置和记忆。"
!insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "在当前用户桌面创建 MemoryDeck 启动图标。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END
Function un.onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd
Section "Uninstall"
  MessageBox MB_OKCANCEL|MB_ICONINFORMATION "卸载将移除程序和随附运行时，保留所有记忆数据。$\r$\n$\r$\n请先关闭正在运行的 MemoryDeck。" IDOK +2
  Abort
  Delete "$DESKTOP\MemoryDeck.lnk"
  Delete "$SMPROGRAMS\MemoryDeck\MemoryDeck.lnk"
  Delete "$SMPROGRAMS\MemoryDeck\使用指南.lnk"
  Delete "$SMPROGRAMS\MemoryDeck\卸载 MemoryDeck.lnk"
  RMDir "$SMPROGRAMS\MemoryDeck"
  Delete "$INSTDIR\MemoryDeck.exe"
  Delete "$INSTDIR\MemoryDeckLauncher.exe"
  Delete "$INSTDIR\使用指南.html"
  Delete "$INSTDIR\runtime-provenance.json"
  ; 保留配置，以便重装后继续使用用户选择的数据目录。
  RMDir /r "$INSTDIR\runtime"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${REGKEY}"
SectionEnd
