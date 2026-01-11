@echo off
REM FF2 Ghidra Headless Analysis Script
REM Imports and analyzes GameAssembly.dll, then decompiles target functions
REM
REM Usage: run_ghidra_analysis.bat [script] [mode]
REM   script:
REM     pathfinding  - Decompile pathfinding and map functions (default)
REM     magic        - Decompile magic/ability growth functions
REM     weapon_skill - Decompile weapon skill growth functions
REM   mode:
REM     import  - Create new project and import GameAssembly.dll (first time)
REM     analyze - Re-run analysis on existing project (subsequent runs)
REM
REM Examples:
REM   run_ghidra_analysis.bat                       - Import + decompile pathfinding
REM   run_ghidra_analysis.bat magic                 - Import + decompile magic
REM   run_ghidra_analysis.bat weapon_skill          - Import + decompile weapon skills
REM   run_ghidra_analysis.bat magic analyze         - Re-analyze existing project for magic
REM   run_ghidra_analysis.bat pathfinding analyze

setlocal enabledelayedexpansion

REM ============================================================================
REM Configuration - Edit these paths if needed
REM ============================================================================
set "GHIDRA_HOME=D:\Games\Dev\ghidra"
set "PROJECT_DIR=D:\Games\Dev\ghidra\projects"
set "PROJECT_NAME=FF2_Analysis"
set "GAME_DIR=D:\Games\steamlibrary\steamapps\common\FINAL FANTASY II PR"
set "GAME_ASSEMBLY=%GAME_DIR%\GameAssembly.dll"
set "SCRIPT_DIR=%~dp0"
set "LOG_FILE=%SCRIPT_DIR%ghidra_analysis.log"

REM ============================================================================
REM Parse arguments
REM ============================================================================
set "SCRIPT_TYPE=pathfinding"
set "MODE=import"

REM First arg: script type
if /i "%~1"=="magic" set "SCRIPT_TYPE=magic"
if /i "%~1"=="pathfinding" set "SCRIPT_TYPE=pathfinding"
if /i "%~1"=="weapon_skill" set "SCRIPT_TYPE=weapon_skill"
if /i "%~1"=="weapon" set "SCRIPT_TYPE=weapon_skill"
if /i "%~1"=="analyze" (
    set "MODE=analyze"
    goto :skip_second_arg
)
if /i "%~1"=="import" (
    set "MODE=import"
    goto :skip_second_arg
)

REM Second arg: mode
if /i "%~2"=="analyze" set "MODE=analyze"
if /i "%~2"=="reanalyze" set "MODE=analyze"
if /i "%~2"=="import" set "MODE=import"

:skip_second_arg

REM Set script-specific variables
if "%SCRIPT_TYPE%"=="magic" (
    set "SCRIPT_FILE=%SCRIPT_DIR%decompile_magic.py"
    set "OUTPUT_FILE=%SCRIPT_DIR%decompiled_magic.c"
    set "SCRIPT_NAME=decompile_magic.py"
) else if "%SCRIPT_TYPE%"=="weapon_skill" (
    set "SCRIPT_FILE=%SCRIPT_DIR%decompile_weapon_skill.py"
    set "OUTPUT_FILE=%SCRIPT_DIR%decompiled_weapon_skill.c"
    set "SCRIPT_NAME=decompile_weapon_skill.py"
) else (
    set "SCRIPT_FILE=%SCRIPT_DIR%decompile_pathfinding.py"
    set "OUTPUT_FILE=%SCRIPT_DIR%decompiled_pathfinding.c"
    set "SCRIPT_NAME=decompile_pathfinding.py"
)

REM ============================================================================
REM Validate paths
REM ============================================================================
echo ======================================================================
echo FF2 Ghidra Headless Analysis
echo ======================================================================
echo.

if not exist "%GHIDRA_HOME%\support\analyzeHeadless.bat" (
    echo ERROR: Ghidra not found at %GHIDRA_HOME%
    exit /b 1
)

if not exist "%GAME_ASSEMBLY%" (
    echo ERROR: GameAssembly.dll not found at %GAME_ASSEMBLY%
    exit /b 1
)

if not exist "%SCRIPT_FILE%" (
    echo ERROR: Python script not found: %SCRIPT_FILE%
    exit /b 1
)

echo Configuration:
echo   Ghidra:         %GHIDRA_HOME%
echo   Project:        %PROJECT_DIR%\%PROJECT_NAME%
echo   GameAssembly:   %GAME_ASSEMBLY%
echo   Script Type:    %SCRIPT_TYPE%
echo   Script:         %SCRIPT_FILE%
echo   Output:         %OUTPUT_FILE%
echo   Log:            %LOG_FILE%
echo   Mode:           %MODE%
echo.

if not exist "%PROJECT_DIR%" mkdir "%PROJECT_DIR%"

echo Starting Ghidra headless analysis...
echo This may take 10-30 minutes for initial import/analysis.
echo Output will be logged to: %LOG_FILE%
echo ======================================================================
echo.

REM Clear previous log
echo [%DATE% %TIME%] Starting Ghidra analysis > "%LOG_FILE%"
echo Script: %SCRIPT_TYPE% >> "%LOG_FILE%"
echo Mode: %MODE% >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

REM Remove trailing backslash from script dir if present
set "SCRIPT_DIR_CLEAN=%SCRIPT_DIR:~0,-1%"

if "%MODE%"=="import" (
    echo Running: Import and analyze GameAssembly.dll
    echo.

    REM Import mode - all on one line to avoid CMD parsing issues
    call "%GHIDRA_HOME%\support\analyzeHeadless.bat" "%PROJECT_DIR%" "%PROJECT_NAME%" -import "%GAME_ASSEMBLY%" -overwrite -scriptPath "%SCRIPT_DIR_CLEAN%" -postScript %SCRIPT_NAME% >> "%LOG_FILE%" 2>&1

) else (
    echo Running: Re-analyze existing project
    echo.

    REM Analyze mode - use -noanalysis since already analyzed, just run script
    call "%GHIDRA_HOME%\support\analyzeHeadless.bat" "%PROJECT_DIR%" "%PROJECT_NAME%" -process "GameAssembly.dll" -noanalysis -scriptPath "%SCRIPT_DIR_CLEAN%" -postScript %SCRIPT_NAME% >> "%LOG_FILE%" 2>&1
)

set "EXIT_CODE=%ERRORLEVEL%"

echo.
echo ======================================================================
echo.
echo === Last 40 lines of log ===
powershell -Command "Get-Content '%LOG_FILE%' -Tail 40"
echo.
echo ======================================================================

if %EXIT_CODE%==0 (
    echo Analysis completed successfully!
    if exist "%OUTPUT_FILE%" (
        echo.
        echo Decompiled output written to:
        echo   %OUTPUT_FILE%
        echo.
        for %%A in ("%OUTPUT_FILE%") do echo File size: %%~zA bytes
    ) else (
        echo.
        echo WARNING: Output file not found at expected location.
        echo Check log for actual output path or errors.
    )
) else (
    echo Analysis failed with exit code %EXIT_CODE%
)
echo ======================================================================

endlocal
exit /b %EXIT_CODE%
