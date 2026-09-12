@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem  HandModel3D build script (.NET 10)
rem  - Output file: build\HandModel3D.exe (single file)
rem  - Requires .NET SDK 10+ to build.
rem  - The produced exe needs the .NET 10 Desktop Runtime (x64)
rem    installed on the target machine.
rem  - Leaves the .csproj untouched; overrides the output
rem    folder here only.
rem ============================================================

set "PROJECT=%~dp0HandModel3D\HandModel3D.csproj"
set "ASSEMBLY_NAME=HandModel3D"
set "CONFIG=Release"
rem  CAUTION: do not name these variables OUTDIR, OUTPUTPATH, etc.
rem  MSBuild imports environment variables as properties, so an
rem  env var named OUTDIR silently overrides the build output dir.
set "BUILD_DIR=%~dp0build"
set "STAGE_DIR=%~dp0HandModel3D\obj\publish-single"

echo(
echo === Build start (%CONFIG% / %ASSEMBLY_NAME%.exe) ===
echo(

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet CLI not found.
    echo         Install the .NET SDK 10 or later.
    exit /b 1
)

rem --- Publish as a framework-dependent single file ---
rem  The publish output goes to a staging folder first: the SDK
rem  writes loose files next to the bundled exe there, so only
rem  what the app needs is copied into the build folder.
rem  (the doubled trailing backslash: MSBuild's arg parser turns
rem  \\" into a single backslash + closing quote)
dotnet publish "%PROJECT%" -c %CONFIG% -r win-x64 --self-contained false ^
    /p:PublishDir="%STAGE_DIR%\\" ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:DebugType=None ^
    /v:minimal /nologo

if errorlevel 1 (
    echo(
    echo [FAILED] Build error occurred.
    exit /b 1
)

if not exist "%STAGE_DIR%\%ASSEMBLY_NAME%.exe" (
    echo [FAILED] Published exe not found in staging folder.
    exit /b 1
)

if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
copy /y "%STAGE_DIR%\%ASSEMBLY_NAME%.exe" "%BUILD_DIR%\%ASSEMBLY_NAME%.exe" >nul
if errorlevel 1 (
    echo [FAILED] Could not copy exe to build folder.
    exit /b 1
)

rem --- Copy runtime data next to the exe ---
rem  The app reads "physical-key-master.json" (capture filename
rem  [row-col] mapping and keyboard layout data) from the exe
rem  directory at startup. The master copy lives in this folder
rem  (HandModel3D\physical-key-master.json).
copy /y "%~dp0physical-key-master.json" "%BUILD_DIR%\physical-key-master.json" >nul
if errorlevel 1 (
    echo [FAILED] Could not copy physical-key-master.json.
    exit /b 1
)

echo(
echo [OK] Build complete
echo      Output: "%BUILD_DIR%\%ASSEMBLY_NAME%.exe"
echo      Data  : "%BUILD_DIR%\physical-key-master.json"
echo(

endlocal
