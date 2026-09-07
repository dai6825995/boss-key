@echo off
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /out:dist\boss-key.exe ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Runtime.Serialization.dll ^
  Program.cs MainForm.cs HideService.cs InputHooks.cs Config.cs WinApi.cs HotkeyTextBox.cs

if errorlevel 1 (
  echo 编译失败
  exit /b 1
)

echo 已生成 dist\boss-key.exe
exit /b 0
