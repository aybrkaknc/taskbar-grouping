@echo off
rem Removes what TaskbarFolders registered outside this folder: the Explorer menu, the
rem start-with-Windows entry, and the groups' shortcuts and Start menu entries. Groups are
rem kept. Run it before deleting the folder; pinned taskbar tiles have to be unpinned by hand.
start "" /wait "%~dp0Manager\TaskbarFolders.Manager.exe" --remove-integration
