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

set APP_VERSION=2026.05.15.024
taskkill /IM MdPad.Wpf.exe /F >nul 2>nul

dotnet build MdPadWv2.sln -c Release
if errorlevel 1 exit /b %errorlevel%

if exist release\app rmdir /s /q release\app
dotnet publish MdPad.Wpf\MdPad.Wpf.csproj -c Release -r win-x64 --self-contained true -o release\app
if errorlevel 1 exit /b %errorlevel%

"C:\Program Files (x86)\NSIS\makensis.exe" installer.nsi
if errorlevel 1 exit /b %errorlevel%

copy /Y "release\MdPadWv2-Setup-%APP_VERSION%.exe" "release\MdPadWv2-Setup.exe" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$nas='\\kait-nas\'+[char]0xacf5+[char]0xc6a9+'\UTIL'; function Stop-MdPadProcesses { Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'MdPadWv2-Setup*' -or $_.ProcessName -eq 'MdPad.Wpf' } | Stop-Process -Force -ErrorAction SilentlyContinue }; function Copy-WithRetry($src,$dst) { for ($i=1; $i -le 5; $i++) { try { Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop; return } catch { Stop-MdPadProcesses; Start-Sleep -Milliseconds (500 * $i); if ($i -eq 5) { throw } } } }; if (Test-Path $nas) { Stop-MdPadProcesses; Copy-WithRetry 'release\MdPadWv2-Setup-%APP_VERSION%.exe' (Join-Path $nas 'MdPadWv2-Setup-%APP_VERSION%.exe'); Copy-WithRetry 'release\MdPadWv2-Setup.exe' (Join-Path $nas 'MdPadWv2-Setup.exe') }"

echo.
echo Setup created: release\MdPadWv2-Setup-%APP_VERSION%.exe
exit /b %errorlevel%
