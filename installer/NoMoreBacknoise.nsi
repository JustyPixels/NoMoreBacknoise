Unicode true
!include "MUI2.nsh"
Name "NoMoreBacknoise++"
OutFile "..\artifacts\NoMoreBacknoise-0.1.0-setup-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\NoMoreBacknoise"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Section "NoMoreBacknoise++" SEC_MAIN
    SetOutPath "$INSTDIR"
    File /r "${BUNDLE}\*.*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateDirectory "$SMPROGRAMS\NoMoreBacknoise++"
    CreateShortcut "$SMPROGRAMS\NoMoreBacknoise++\NoMoreBacknoise++.lnk" "$INSTDIR\NoMoreBacknoise.exe"
    CreateShortcut "$SMPROGRAMS\NoMoreBacknoise++\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "DisplayName" "NoMoreBacknoise++"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "DisplayVersion" "0.1.0"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "Publisher" "JustPixels"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise" "NoRepair" 1
SectionEnd
Section "Uninstall"
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "NoMoreBacknoise"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NoMoreBacknoise"
    Delete "$SMPROGRAMS\NoMoreBacknoise++\NoMoreBacknoise++.lnk"
    Delete "$SMPROGRAMS\NoMoreBacknoise++\Uninstall.lnk"
    RMDir "$SMPROGRAMS\NoMoreBacknoise++"
    !include "${UNINSTALL_MANIFEST}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
    # Settings remain in LOCALAPPDATA\NoMoreBacknoise. No cable/driver is removed.
SectionEnd
