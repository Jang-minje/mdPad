@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet SDK/runtime was not found.
  exit /b 1
)

if not exist "C:\Program Files (x86)\NSIS\makensis.exe" (
  echo NSIS makensis.exe was not found.
  echo Install NSIS or update build.bat with the correct path.
  exit /b 1
)

set APP_VERSION=2026.05.15.008
taskkill /IM MdPad.Wpf.exe /F >nul 2>nul

dotnet build MdPadWv2.sln -c Release
if errorlevel 1 exit /b %errorlevel%

if exist release\app rmdir /s /q release\app
dotnet publish MdPad.Wpf\MdPad.Wpf.csproj -c Release -r win-x64 --self-contained true -o release\app
if errorlevel 1 exit /b %errorlevel%

"C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
if errorlevel 1 exit /b %errorlevel%

copy /Y "release\MdPadWv2-Setup-%APP_VERSION%.exe" "release\MdPadWv2-Setup.exe" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$nas='\\kait-nas\'+[char]0xacf5+[char]0xc6a9+'\UTIL'; if (Test-Path $nas) { Copy-Item -LiteralPath 'release\MdPadWv2-Setup-%APP_VERSION%.exe' -Destination (Join-Path $nas 'MdPadWv2-Setup-%APP_VERSION%.exe') -Force; Copy-Item -LiteralPath 'release\MdPadWv2-Setup.exe' -Destination (Join-Path $nas 'MdPadWv2-Setup.exe') -Force }"

echo.
echo Setup created: release\MdPadWv2-Setup-%APP_VERSION%.exe
exit /b %errorlevel%
