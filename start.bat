@echo off
chcp 65001 > nul
echo ==========================================
echo    YOLO Annotator - AI Assisted Annotation Tool
echo ==========================================
echo.

echo [1/2] Starting backend service...
cd backend
start "Yolo Annotator Backend" python start.py
cd ..

echo Waiting for backend to start...
timeout /t 3 /nobreak > nul

echo.
echo [2/2] Starting frontend application...
dotnet run

echo.
echo Frontend closed. Backend is still running in background window.
echo To stop backend, close the backend window or press Ctrl+C there.
pause
