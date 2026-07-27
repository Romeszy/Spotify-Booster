Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.2.0"
!endif
!ifndef SOURCE_DIR
  !error "SOURCE_DIR must point to the portable publish directory."
!endif
!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "..\artifacts\installer\SpotifyBooster-Setup-x64.exe"
!endif
!ifndef APP_ICON
  !define APP_ICON "..\src\SpotifyBooster.App\Assets\SpotifyBooster.ico"
!endif

!define APP_NAME "Spotify Booster"
!define APP_EXE "SpotifyBooster.exe"
!define APP_ID "SpotifyBooster"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"

Name "${APP_NAME}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Spotify Booster"
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} per-user installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "Spotify Booster"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 Spotify Booster contributors"

!define MUI_ABORTWARNING
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "French"

Section "${APP_NAME} (required)" SEC_MAIN
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /oname=${APP_EXE} "${SOURCE_DIR}\SpotifyBooster-Portable-x64.exe"
  File "${SOURCE_DIR}\appsettings.json"
  File "${SOURCE_DIR}\LICENSE.txt"
  File "${SOURCE_DIR}\THIRD_PARTY_NOTICES.md"
  File /oname=SpotifyBooster.ico "${APP_ICON}"

  SetOutPath "$INSTDIR\licenses"
  File "${SOURCE_DIR}\licenses\NAUDIO-LICENSE.txt"
  File "${SOURCE_DIR}\licenses\DOTNET-LICENSE.txt"
  File "${SOURCE_DIR}\licenses\DOTNET-THIRD-PARTY-NOTICES.txt"
  File "${SOURCE_DIR}\licenses\NSIS-LICENSE.txt"
  SetOutPath "$INSTDIR"

  WriteRegStr HKCU "Software\SpotifyBooster" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Spotify Booster"
  CreateShortcut "$SMPROGRAMS\Spotify Booster\Spotify Booster.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\SpotifyBooster.ico"
  CreateShortcut "$SMPROGRAMS\Spotify Booster\Uninstall Spotify Booster.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\SpotifyBooster.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Spotify Booster"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Spotify Booster"
  StrCpy $1 '"$INSTDIR\${APP_EXE}" --minimized'
  StrCmp $0 $1 0 +2
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Spotify Booster"
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
  CreateShortcut "$DESKTOP\Spotify Booster.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\SpotifyBooster.ico"
SectionEnd

Section /o "Start automatically when I sign in" SEC_AUTOSTART
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Spotify Booster" '"$INSTDIR\${APP_EXE}" --minimized'
SectionEnd

LangString DESC_MAIN ${LANG_ENGLISH} "Installs Spotify Booster for the current Windows user. No administrator access is required."
LangString DESC_DESKTOP ${LANG_ENGLISH} "Creates a shortcut on the desktop."
LangString DESC_AUTOSTART ${LANG_ENGLISH} "Starts Spotify Booster automatically after you sign in."
LangString DESC_MAIN ${LANG_FRENCH} "Installe Spotify Booster pour l'utilisateur Windows actuel. Aucun accès administrateur n'est requis."
LangString DESC_DESKTOP ${LANG_FRENCH} "Crée un raccourci sur le Bureau."
LangString DESC_AUTOSTART ${LANG_FRENCH} "Lance Spotify Booster automatiquement à l'ouverture de session."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} $(DESC_MAIN)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} $(DESC_DESKTOP)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AUTOSTART} $(DESC_AUTOSTART)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Spotify Booster"
  StrCpy $1 '"$INSTDIR\${APP_EXE}" --minimized'
  StrCmp $0 $1 0 +2
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Spotify Booster"
  Delete "$DESKTOP\Spotify Booster.lnk"
  RMDir /r "$SMPROGRAMS\Spotify Booster"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.md"
  Delete "$INSTDIR\licenses\NAUDIO-LICENSE.txt"
  Delete "$INSTDIR\licenses\DOTNET-LICENSE.txt"
  Delete "$INSTDIR\licenses\DOTNET-THIRD-PARTY-NOTICES.txt"
  Delete "$INSTDIR\licenses\NSIS-LICENSE.txt"
  RMDir "$INSTDIR\licenses"
  Delete "$INSTDIR\SpotifyBooster.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "Software\SpotifyBooster"

  IfSilent preserveConfig
  MessageBox MB_YESNO|MB_ICONQUESTION "Delete local settings and logs in $LOCALAPPDATA\SpotifyBooster?" IDNO preserveConfig
  RMDir /r "$LOCALAPPDATA\SpotifyBooster"
preserveConfig:
SectionEnd
