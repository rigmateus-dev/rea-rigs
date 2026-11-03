@echo off
rem REA de Rig's - desinstallation. Garde Documents\Analyse (tes resultats) et ne touche pas a Docker.
setlocal
taskkill /im "REA de Rigs.exe" /f >nul 2>&1
"%LOCALAPPDATA%\Programs\REA de Rigs\REA de Rigs.exe" --debrancher
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\REA de Rig's.lnk" >nul 2>&1
for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "[Environment]::GetFolderPath('Desktop')"`) do del "%%D\REA de Rig's.lnk" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\REA de Rigs" /f >nul 2>&1
rd /s /q "%LOCALAPPDATA%\REA de Rigs" >nul 2>&1
rd /s /q "%APPDATA%\REA de Rigs" >nul 2>&1
echo REA de Rig's est desinstalle. Tes analyses restent dans Documents\Analyse.
echo REA a aussi ete retire de l'appli Claude (copie de sa config gardee).
start "" cmd /c "timeout /t 2 >nul & rd /s /q \"%LOCALAPPDATA%\Programs\REA de Rigs\""
