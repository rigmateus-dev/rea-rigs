@echo off
rem REA de Rig's - installation pour cet utilisateur (pas besoin d'administrateur)
rem  -> %LOCALAPPDATA%\Programs\REA de Rigs, raccourcis Menu Demarrer + Bureau, entree « Applications installees »
setlocal
cd /d "%~dp0"
call build.cmd || exit /b 1
set "DST=%LOCALAPPDATA%\Programs\REA de Rigs"
taskkill /im "REA de Rigs.exe" /f >nul 2>&1
if not exist "%DST%" mkdir "%DST%"
copy /y "bin\REA de Rigs.exe" "%DST%\REA de Rigs.exe" >nul || exit /b 1
copy /y "uninstall.cmd" "%DST%\desinstaller.cmd" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$d=$env:LOCALAPPDATA+'\Programs\REA de Rigs'; $exe=$d+'\REA de Rigs.exe'; $w=New-Object -ComObject WScript.Shell;" ^
  "foreach($p in @([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('Desktop'))){ $s=$w.CreateShortcut($p+'\REA de Rig''s.lnk'); $s.TargetPath=$exe; $s.WorkingDirectory=$d; $s.IconLocation=$exe+',0'; $s.Description='Analyse d''applications dans une boite fermee'; $s.Save() };" ^
  "$k='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\REA de Rigs'; New-Item $k -Force | Out-Null;" ^
  "Set-ItemProperty $k DisplayName 'REA de Rig''s'; Set-ItemProperty $k Publisher 'Rigmateus'; Set-ItemProperty $k DisplayIcon ($exe+',0');" ^
  "Set-ItemProperty $k InstallLocation $d; Set-ItemProperty $k UninstallString ('cmd /c \"'+$d+'\desinstaller.cmd\"'); Set-ItemProperty $k NoModify 1 -Type DWord; Set-ItemProperty $k NoRepair 1 -Type DWord;" ^
  "Set-ItemProperty $k DisplayVersion '1.0'"
echo REA de Rig's installe.
start "" "%DST%\REA de Rigs.exe"
