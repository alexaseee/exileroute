@echo off
setlocal
set "compiler=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" (
    echo .NET Framework C# compiler was not found.
    exit /b 1
)
"%compiler%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%~dp0PoEActOverlay.exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /resource:"%~dp0CampaignAreas.tsv",CampaignAreas.tsv /resource:"%~dp0GemCatalog.tsv",GemCatalog.tsv /resource:"%~dp0BuiltInProfile.json",BuiltInProfile.json "%~dp0OverlayApp.cs" "%~dp0Theme.cs" "%~dp0ClientLog.cs" "%~dp0CampaignAreas.cs" "%~dp0RouteEditor.cs" "%~dp0RegexEditor.cs" "%~dp0Profiles.cs" "%~dp0UiText.cs" "%~dp0GemPickerDialog.cs"
exit /b %errorlevel%
