@echo off
chcp 65001 > nul
echo ==========================================
echo    YOLO Annotator - 打包发布版本
echo ==========================================
echo.

REM 设置发布配置
set CONFIGURATION=Release
set OUTPUT_DIR=Release_Package
set PROJECT_NAME=YoloAnnotator

echo [1/5] 清理旧的发布目录...
if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%"
)

echo [2/5] 发布前端应用程序...
dotnet publish YoloAnnotator.csproj --configuration %CONFIGURATION% --output "%OUTPUT_DIR%\frontend" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false

if %ERRORLEVEL% NEQ 0 (
    echo 发布失败！
    pause
    exit /b 1
)

echo [3/5] 复制后端文件...
mkdir "%OUTPUT_DIR%\backend"
xcopy /E /I /Y backend\annotations "%OUTPUT_DIR%\backend\annotations" 2>nul
xcopy /E /I /Y backend\configs "%OUTPUT_DIR%\backend\configs" 2>nul
xcopy /E /I /Y backend\datasets "%OUTPUT_DIR%\backend\datasets" 2>nul
xcopy /E /I /Y backend\images "%OUTPUT_DIR%\backend\images" 2>nul
xcopy /E /I /Y backend\models "%OUTPUT_DIR%\backend\models" 2>nul
xcopy /E /I /Y backend\services "%OUTPUT_DIR%\backend\services" 2>nul
copy /Y backend\*.py "%OUTPUT_DIR%\backend\" 2>nul
copy /Y backend\requirements.txt "%OUTPUT_DIR%\backend\" 2>nul
copy /Y backend\*.pt "%OUTPUT_DIR%\backend\" 2>nul

echo [4/5] 创建发布版启动脚本...
echo @echo off > "%OUTPUT_DIR%\启动.bat"
echo chcp 65001 ^> nul >> "%OUTPUT_DIR%\启动.bat"
echo echo YOLO Annotator - AI 辅助标注工具 >> "%OUTPUT_DIR%\启动.bat"
echo echo. >> "%OUTPUT_DIR%\启动.bat"
echo echo [1/2] 启动后端服务... >> "%OUTPUT_DIR%\启动.bat"
echo cd backend >> "%OUTPUT_DIR%\启动.bat"
echo start "Yolo Annotator Backend" python start.py >> "%OUTPUT_DIR%\启动.bat"
echo cd .. >> "%OUTPUT_DIR%\启动.bat"
echo echo. >> "%OUTPUT_DIR%\启动.bat"
echo echo 等待后端启动... >> "%OUTPUT_DIR%\启动.bat"
echo timeout /t 3 /nobreak ^> nul >> "%OUTPUT_DIR%\启动.bat"
echo echo. >> "%OUTPUT_DIR%\启动.bat"
echo echo [2/2] 启动前端应用程序... >> "%OUTPUT_DIR%\启动.bat"
echo start "" frontend\%PROJECT_NAME%.exe >> "%OUTPUT_DIR%\启动.bat"
echo echo. >> "%OUTPUT_DIR%\启动.bat"
echo echo 前端已启动！后端在后台运行。 >> "%OUTPUT_DIR%\启动.bat"
echo echo. >> "%OUTPUT_DIR%\启动.bat"
echo pause >> "%OUTPUT_DIR%\启动.bat"

echo [5/5] 创建安装说明...
echo YOLO Annotator - AI 辅助标注工具 > "%OUTPUT_DIR%\使用说明.txt"
echo ========================================== >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 安装步骤： >> "%OUTPUT_DIR%\使用说明.txt"
echo 1. 确保已安装 Python 3.8 或更高版本 >> "%OUTPUT_DIR%\使用说明.txt"
echo 2. 安装后端依赖： >> "%OUTPUT_DIR%\使用说明.txt"
echo    cd backend >> "%OUTPUT_DIR%\使用说明.txt"
echo    pip install -r requirements.txt >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 使用方法： >> "%OUTPUT_DIR%\使用说明.txt"
echo 1. 双击 "启动.bat" 启动应用 >> "%OUTPUT_DIR%\使用说明.txt"
echo 2. 前端窗口会自动打开 >> "%OUTPUT_DIR%\使用说明.txt"
echo 3. 后端服务会在后台运行 >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 注意事项： >> "%OUTPUT_DIR%\使用说明.txt"
echo - 首次运行需要安装 Python 依赖包 >> "%OUTPUT_DIR%\使用说明.txt"
echo - 如果有 NVIDIA 显卡，可以安装 CUDA 版本的 PyTorch 加速训练 >> "%OUTPUT_DIR%\使用说明.txt"
echo - 关闭前端后，请手动关闭后端窗口 >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 模型文件： >> "%OUTPUT_DIR%\使用说明.txt"
echo - yolo26*.pt: 自定义 YOLOv26 模型（检测、分割、姿态估计、旋转框） >> "%OUTPUT_DIR%\使用说明.txt"
echo - yolov8*.pt: 标准 YOLOv8 模型 >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 支持的标注类型： >> "%OUTPUT_DIR%\使用说明.txt"
echo - 目标检测（矩形框） >> "%OUTPUT_DIR%\使用说明.txt"
echo - 实例分割（多边形） >> "%OUTPUT_DIR%\使用说明.txt"
echo - 姿态估计（关键点） >> "%OUTPUT_DIR%\使用说明.txt"
echo - 旋转框检测 >> "%OUTPUT_DIR%\使用说明.txt"
echo - 圆形、线段、点标注 >> "%OUTPUT_DIR%\使用说明.txt"
echo. >> "%OUTPUT_DIR%\使用说明.txt"
echo 如有问题，请查看后端窗口的错误信息。 >> "%OUTPUT_DIR%\使用说明.txt"

echo.
echo ==========================================
echo 打包完成！
echo ==========================================
echo.
echo 发布目录: %OUTPUT_DIR%\
echo 前端文件: %OUTPUT_DIR%\frontend\
echo 后端文件: %OUTPUT_DIR%\backend\
echo 启动文件: %OUTPUT_DIR%\启动.bat
echo 说明文件: %OUTPUT_DIR%\使用说明.txt
echo.
echo 重要提示：
echo 1. 请确保接收者已安装 Python 3.8+
echo 2. 首次运行前需要安装依赖：cd backend ^&^& pip install -r requirements.txt
echo 3. 模型文件 (*.pt) 已包含在后端目录中
echo.
pause