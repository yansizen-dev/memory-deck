Unicode true
!include "LogicLib.nsh"
!include "x64.nsh"
Name "MemoryDeck"
OutFile "payload/MemoryDeckLauncher.exe"
Icon "app.ico"
RequestExecutionLevel user
SilentInstall silent
AutoCloseWindow true
VIProductVersion "1.2.2.0"
VIAddVersionKey /LANG=2052 "ProductName" "MemoryDeck 启动器"
VIAddVersionKey /LANG=2052 "FileDescription" "使用随附运行时启动 MemoryDeck"
VIAddVersionKey /LANG=2052 "FileVersion" "1.2.2.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "MemoryDeck"
Section
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "MemoryDeck 需要 64 位 Windows。"
    SetErrorLevel 1
    Quit
  ${EndIf}
  IfFileExists "$EXEDIR\runtime\host\fxr\10.0.13\hostfxr.dll" runtime_ok
  MessageBox MB_ICONSTOP "随附运行时缺失，请重新安装 MemoryDeck。记忆数据会保留。"
  SetErrorLevel 1
  Quit
runtime_ok:
  System::Call 'kernel32::SetEnvironmentVariableW(w "DOTNET_ROOT_X64", w "$EXEDIR\runtime") i.r0'
  System::Call 'kernel32::SetEnvironmentVariableW(w "DOTNET_ROOT", w "$EXEDIR\runtime") i.r0'
  SetOutPath "$EXEDIR"
  ClearErrors
  Exec '"$EXEDIR\MemoryDeck.exe"'
  IfErrors launch_error done
launch_error:
  MessageBox MB_ICONSTOP "无法启动 MemoryDeck。请关闭旧程序后重新安装。"
  SetErrorLevel 1
done:
SectionEnd
