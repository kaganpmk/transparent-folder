@echo off
title Transparent Folder - Uninstall
echo.
echo Stopping Transparent Folder and removing startup / context menu entries...
"%~dp0TransparentFolder.exe" --uninstall
echo.
echo Done! Transparent Folder has been unregistered.
echo You can now safely delete this folder.
echo (Your saved folders remain in %LOCALAPPDATA%\TransparentFolder unless you delete it.)
echo.
pause
