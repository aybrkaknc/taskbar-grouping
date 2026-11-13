@echo off
rem Hot reload: dotnet watch rebuilds and restarts the app when a source file changes.
rem Double-click to run. Ctrl+C stops it.
setlocal
cd /d "%~dp0"

echo.
echo  1) Manager
echo  2) Launcher (grup kimligi gerekir)
echo.
set /p "choice=Secim (1/2): "

if "%choice%"=="1" goto manager
if "%choice%"=="2" goto launcher

echo Gecersiz secim.
pause
exit /b 1

:manager
dotnet watch run --project "src\TaskbarFolders.Manager"
goto end

:launcher
set /p "groupId=Grup kimligi (GUID): "
if "%groupId%"=="" (
    echo Grup kimligi bos olamaz.
    pause
    exit /b 1
)
dotnet watch run --project "src\TaskbarFolders.Launcher" -- --group-id %groupId%
goto end

:end
echo.
pause
endlocal
