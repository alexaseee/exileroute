Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!ifndef APP_VERSION
    !error "Set APP_VERSION or run build-installer.ps1."
!endif

!define APP_NAME "ExileRoute"
!define APP_EXE "ExileRoute.exe"
!define ARP_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PoEActOverlay"

Name "${APP_NAME}"
OutFile "dist\ExileRoute-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\PoEActOverlay"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "ExileRoute Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 alexaseee"

!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_ICON "ExileRoute.ico"
!define MUI_UNICON "ExileRoute.ico"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "English"

LangString UnsupportedWindows ${LANG_RUSSIAN} "Требуется Windows 10 или 11 x64."
LangString UnsupportedWindows ${LANG_ENGLISH} "Windows 10 or 11 x64 is required."
LangString MissingFramework ${LANG_RUSSIAN} "Установите Microsoft .NET Framework 4.8 или новее и повторите установку."
LangString MissingFramework ${LANG_ENGLISH} "Install Microsoft .NET Framework 4.8 or later and run Setup again."
LangString CloseOverlay ${LANG_RUSSIAN} "Закройте ExileRoute и повторите операцию."
LangString CloseOverlay ${LANG_ENGLISH} "Close ExileRoute and try again."

!macro RequireClosed
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\PoEActOverlay") p.r0'
    ${If} $0 != 0
        System::Call 'kernel32::CloseHandle(p r0)'
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(CloseOverlay)" /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
!macroend

Function .onInit
    ${IfNot} ${RunningX64}
    ${OrIfNot} ${AtLeastWin10}
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(UnsupportedWindows)" /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
    SetRegView 64
    ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    ${If} $0 < 528040
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(MissingFramework)" /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
FunctionEnd

Section "Install"
    !insertmacro RequireClosed
    SetShellVarContext current
    SetRegView 64
    SetOutPath "$INSTDIR"
    File "${APP_EXE}"
    File "LICENSE"
    File "THIRD_PARTY_NOTICES.md"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

    WriteRegStr HKCU "${ARP_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${ARP_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${ARP_KEY}" "Publisher" "alexaseee"
    WriteRegStr HKCU "${ARP_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${ARP_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${ARP_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKCU "${ARP_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${ARP_KEY}" "NoRepair" 1

    ; Remove the old executable and shortcut after installing the renamed app.
    Delete "$INSTDIR\PoEActOverlay.exe"
    Delete "$SMPROGRAMS\PoE Act Overlay.lnk"
SectionEnd

Function un.onInit
    !insertmacro RequireClosed
FunctionEnd

Section "Uninstall"
    SetShellVarContext current
    SetRegView 64
    Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\LICENSE"
    Delete "$INSTDIR\THIRD_PARTY_NOTICES.md"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
    DeleteRegKey HKCU "${ARP_KEY}"
SectionEnd
