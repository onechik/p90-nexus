@echo off
title P90 Nexus Console
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\P90-Console.ps1"
if errorlevel 1 pause
