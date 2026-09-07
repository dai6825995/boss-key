@echo off
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /out:dist\boss-key.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll Program.cs MainForm.cs HideService.cs InputHooks.cs Config.cs WinApi.cs HotkeyTextBox.cs WindowFx.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo Built dist\boss-key.exe
exit /b 0
