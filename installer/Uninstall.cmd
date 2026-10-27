@echo off
rem Uninstalls this portable copy of TaskbarFolders and everything it added to Windows:
rem taskbar tiles, the Explorer menu, the start-with-Windows entry, Start menu entries,
rem the groups and settings in %APPDATA%\TaskbarFolders, and finally this folder's own files.
setlocal
set "HERE=%~dp0"
cd /d "%TEMP%"

rem The Manager asks for confirmation, does the work, and reports. Go on only on 0 (done)
rem or 1 (done, something left): 2 is "the user said no", and anything else - a crash, a
rem killed process, a missing Manager - must not lead to deleting files nobody agreed to.
start "" /wait "%HERE%Manager\TaskbarFolders.Manager.exe" --uninstall
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" if not "%RESULT%"=="1" exit /b %RESULT%

rem What the Manager could not delete while it ran: the runtime files .NET unpacks for the
rem single-file executables, and its own last log lines.
rd /s /q "%TEMP%\.net\TaskbarFolders.Manager" 2>nul
rd /s /q "%TEMP%\.net\TaskbarFolders.Launcher" 2>nul
rd /s /q "%APPDATA%\TaskbarFolders" 2>nul

rem Only the folders this ZIP contains; anything else put here is left alone.
rd /s /q "%HERE%Manager" 2>nul
rd /s /q "%HERE%Launcher" 2>nul

rem Last: this script, then the folder if nothing else is in it. "(goto)" ends the batch
rem first, so deleting the running script does not confuse cmd.
(goto) 2>nul & del "%~f0" & rd "%HERE%" 2>nul
