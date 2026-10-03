@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_cable_path_experiment.ps1"
if errorlevel 1 pause
