@echo off
rem Baut dist\DERO-Patcher.exe mit dem C#-Compiler, der in Windows (.NET Framework 4) enthalten ist.
rem Die Oberflaeche (ui\index.html) wird als Ressource in die .exe eingebettet.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%~dp0dist\DERO-Patcher.exe" ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  /resource:"%~dp0ui\index.html",index.html ^
  "%~dp0src\*.cs"
exit /b %ERRORLEVEL%
