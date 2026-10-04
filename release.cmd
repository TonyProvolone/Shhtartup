@echo off
rem Double-click or run from a terminal to publish a release. See scripts\release.ps1 for what it does.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\release.ps1" %*
