@echo off
rem Asks for admin rights, then runs Fix-DriveLetters.ps1 (pins external drives to fixed letters by volume label)
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Fix-DriveLetters.ps1"
