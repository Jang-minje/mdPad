Unicode true
!define APP_NAME "MD Pad WV2"
!define APP_EXE "MdPad.Wpf.exe"
!define INSTALL_DIR "$LOCALAPPDATA\Programs\MdPadWv2"
!define APP_VERSION "2026.06.23.003"

Name "${APP_NAME}"
OutFile "release\MdPadWv2-Setup-${APP_VERSION}.exe"
InstallDir "${INSTALL_DIR}"
RequestExecutionLevel user

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  ExecWait 'taskkill /IM "${APP_EXE}" /F'
  Sleep 500
  SetOutPath "$INSTDIR"
  File /r "release\app\*.*"
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MdPadWv2" '"$INSTDIR\${APP_EXE}" --tray'
  WriteRegStr HKCU "Software\Classes\mdpad" "" "URL:MD Pad Protocol"
  WriteRegStr HKCU "Software\Classes\mdpad" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\mdpad\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'
  WriteRegStr HKCU "Software\Classes\.md" "" "MdPadWv2.Markdown"
  WriteRegStr HKCU "Software\Classes\.markdown" "" "MdPadWv2.Markdown"
  WriteRegStr HKCU "Software\Classes\MdPadWv2.Markdown" "" "Markdown Document"
  WriteRegStr HKCU "Software\Classes\MdPadWv2.Markdown\DefaultIcon" "" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "Software\Classes\MdPadWv2.Markdown\shell\open\command" "" '"$INSTDIR\${APP_EXE}" "%1"'
  Exec "$INSTDIR\${APP_EXE}"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MdPadWv2"
  DeleteRegKey HKCU "Software\Classes\mdpad"
  DeleteRegKey HKCU "Software\Classes\MdPadWv2.Markdown"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
  RMDir /r "$INSTDIR"
SectionEnd
