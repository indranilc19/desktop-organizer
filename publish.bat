@echo off
rem Publishes this folder to GitHub with Git for Windows (Git Credential Manager handles the sign-in in your browser).
rem Usage: publish.bat https://github.com/<you>/desktop-organizer.git
setlocal
cd /d "%~dp0"
set REMOTE=%~1
if "%REMOTE%"=="" set /p REMOTE=GitHub repository URL (https://github.com/you/desktop-organizer.git): 
where git >nul 2>&1 || (echo Git for Windows is not installed: https://git-scm.com/download/win & pause & exit /b 1)
if not exist .git (
  git init -b main
  git config user.name >nul 2>&1 || git config user.name "Desktop Organizer"
  git config user.email >nul 2>&1 || git config user.email "desktop-organizer@users.noreply.github.com"
)
git add -A
git commit -m "Desktop Organizer: launcher, drive catalogue, media library tools" || echo (nothing new to commit)
git remote remove origin >nul 2>&1
git remote add origin %REMOTE%
git push -u origin main
echo.
if errorlevel 1 (echo Push failed - see the message above.) else (echo Published to %REMOTE%)
pause
