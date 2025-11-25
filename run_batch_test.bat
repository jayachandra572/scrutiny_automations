@echo off
echo ╔══════════════════════════════════════════════════════════════╗
echo ║          Running BatchProcessor Test                        ║
echo ╚══════════════════════════════════════════════════════════════╝
echo.

cd /d "%~dp0"

echo Current directory: %CD%
echo.

BatchProcessor.exe "C:\Users\Jaya\Documents\AutoCADTests\input_drawings" "C:\Users\Jaya\Documents\AutoCADTests\output_results" "C:\Users\Jaya\Documents\AutoCADTests\config.json"

echo.
echo ╔══════════════════════════════════════════════════════════════╗
echo ║          Test Complete                                       ║
echo ╚══════════════════════════════════════════════════════════════╝
pause



