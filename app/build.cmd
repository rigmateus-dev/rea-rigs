@echo off
rem REA de Rig's - compile l'appli (aucun outil a installer : le compilateur C# est dans Windows)
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "WPF=%FW%\WPF"
if not exist bin mkdir bin
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:"%~dp0res\app.ico" /out:"%~dp0bin\REA de Rigs.exe" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\*.cs > build.log 2>&1
if errorlevel 1 ( echo ECHEC compilation >> build.log & type build.log & exit /b 1 )
echo compilation OK >> build.log
echo compilation OK
exit /b 0
