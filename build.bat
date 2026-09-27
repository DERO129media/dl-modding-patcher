@echo off
rem Baut dist\DERO-WurfPatcher.exe mit dem C#-Compiler, der in Windows (.NET Framework 4) enthalten ist.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%~dp0dist\DERO-WurfPatcher.exe" ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  "%~dp0src\Patcher.cs"
