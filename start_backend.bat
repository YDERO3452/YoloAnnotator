@echo off
chcp 65001 > nul
echo ==========================================
echo    Start YOLO Annotator Backend
echo ==========================================
echo.

cd backend
echo Checking Python environment...
python --version

echo.
echo Checking dependencies...
pip show fastapi > nul 2>&1
if errorlevel 1 (
    echo Installing dependencies...
    pip install -r requirements.txt
)

echo.
echo Starting backend service (http://localhost:8000)...
python start.py

pause