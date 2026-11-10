@echo off
setlocal
set "compiler=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" (
    echo .NET Framework C# compiler was not found.
    exit /b 1
)
set "overlay_output=ExileRoute.exe"
set "overlay_define="
if /i "%~1"=="author" (
    set "overlay_output=ExileRoute-Author.exe"
    set "overlay_define=/define:EDIT_DEFAULT_PROFILE"
)
"%compiler%" /nologo /codepage:65001 /target:winexe /optimize+ %overlay_define% /out:"%~dp0%overlay_output%" /win32icon:"%~dp0ExileRoute.ico" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /resource:"%~dp0CampaignAreas.tsv",CampaignAreas.tsv /resource:"%~dp0GemCatalog.tsv",GemCatalog.tsv /resource:"%~dp0BuiltInProfile.json",BuiltInProfile.json /resource:"%~dp0ExileLevelingProfile.json",ExileLevelingProfile.json "%~dp0OverlayApp.cs" "%~dp0Theme.cs" "%~dp0ClientLog.cs" "%~dp0CampaignAreas.cs" "%~dp0RouteEditor.cs" "%~dp0RegexEditor.cs" "%~dp0Profiles.cs" "%~dp0UiText.cs" "%~dp0GemPickerDialog.cs"
exit /b %errorlevel%
