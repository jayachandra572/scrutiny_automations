@echo off
REM Build Release Script for BatchProcessor (Batch File Version)

echo ========================================
echo   BatchProcessor Release Build
echo ========================================
echo.

REM Check if .NET SDK is installed
echo Checking .NET SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Please install .NET 8.0 SDK.
    pause
    exit /b 1
)

echo .NET SDK found
echo.

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean -c Release >nul 2>&1
if exist "bin\Release" (
    rmdir /s /q "bin\Release" 2>nul
)
echo Cleaned
echo.

REM Build and publish
echo Building Release version...
echo   Framework-Dependent (users need .NET 8.0 Runtime)
echo.

dotnet publish -c Release -r win-x64 --self-contained false

if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Build Complete!
echo ========================================
echo.
echo Output location: bin\Release\net8.0-windows\win-x64\publish
echo.
echo Next steps:
echo   1. Review files in the publish folder
echo   2. Run create-distribution.ps1 to create distribution package
echo.
pause

