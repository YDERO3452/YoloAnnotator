@echo off
chcp 65001 > nul
echo ==========================================
echo    Start YOLO Annotator Frontend
echo ==========================================
echo.

echo Checking .NET environment...
dotnet --version

echo.
echo Starting application...
dotnet run

pause