@echo off
setlocal

set "ROOT=%~dp0"
set "FONT_ROOT=%ROOT%..\SPT-FontReplace"
set "CONFIGURATIONS=Debug-3.11 Debug-4.0 Debug-4.1 Release-3.11 Release-4.0 Release-4.1"

for %%C in (%CONFIGURATIONS%) do (
    echo Building Subtitle %%C...
    dotnet build "%ROOT%Subtitle.sln" -c %%C
    if errorlevel 1 exit /b 1
)

for %%C in (%CONFIGURATIONS%) do (
    echo Building FontReplace %%C...
    dotnet build "%FONT_ROOT%\FontReplace.sln" -c %%C
    if errorlevel 1 exit /b 1
)

echo All version configurations built successfully.
exit /b 0
