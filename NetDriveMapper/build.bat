@echo off
setlocal
cd /d "%~dp0"

set CSC=
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not defined CSC (
  echo Не найден компилятор C# ^(csc.exe^). Нужна Windows с .NET Framework 4.
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /win32icon:"%~dp0network-drive.ico" /out:"%~dp0NetDriveMapper.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0NetDriveMapper.cs"
if errorlevel 1 (
  echo Сборка не удалась.
  pause
  exit /b 1
)

echo Готово: %~dp0NetDriveMapper.exe
echo.
echo Первый запуск — окно с путями шаров и именами ярлыков.
echo Дальше — отключение, удаление ярлыков, повторное подключение.
echo Чтобы снова открыть настройки: NetDriveMapper.exe /setup  или запуск с зажатым Ctrl.
echo.
pause
