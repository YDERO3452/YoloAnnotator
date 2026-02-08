# YOLO Annotator - 智能图像标注工具

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.8+-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![FastAPI](https://img.shields.io/badge/FastAPI-0.100+-009688?logo=fastapi&logoColor=white)](https://fastapi.tiangolo.com/)
[![PyTorch](https://img.shields.io/badge/PyTorch-2.0+-EE4C2C?logo=pytorch&logoColor=white)](https://pytorch.org/)
[![YOLO](https://img.shields.io/badge/YOLO-v8%20%7C%20v26-00FFFF)](https://github.com/ultralytics/ultralytics)

一个功能强大的 YOLO 图像标注和训练工具，支持多种标注类型、AI 辅助标注、模型训练和实时检测。

## 项目简介

YOLO Annotator 是一个集成了图像标注、模型训练和目标检测的完整解决方案。它提供了直观的 WPF 桌面应用程序界面和强大的 FastAPI 后端服务，支持多种 YOLO 模型和标注格式。

### 主要特性

- **多种标注类型**
  - 矩形边界框 (Bounding Box)
  - 旋转框 (Oriented Bounding Box)
  - 多边形分割 (Polygon Segmentation)
  - 关键点标注 (Keypoints/Pose)
  - 圆形、线段、点标注

- **AI 辅助标注**
  - 自动检测和标注
  - 批量 AI 标注

- **模型训练**
  - 支持 YOLOv8/YOLO26 系列模型
  - 检测、分割、OBB 任务
  - GPU/CPU 训练
  - 丰富的数据增强选项
  - 早停机制

- **实时检测**
  - 图像检测
  - 视频检测
  - 摄像头实时检测

- **格式导出**
  - YOLO 格式

## 技术栈

### 前端 (WPF 桌面应用)
- **.NET 10.0** - 最新的 .NET 框架
- **WPF** - Windows Presentation Foundation
- **C#** - 应用程序逻辑
- **RestSharp** - HTTP 客户端
- **Newtonsoft.Json** - JSON 处理

### 后端 (FastAPI 服务)
- **Python 3.8+**
- **FastAPI** - 现代 Web 框架
- **Ultralytics** - YOLO 模型库
- **PyTorch** - 深度学习框架
- **OpenCV** - 图像处理
- **Pillow** - 图像操作

## 项目结构

```
YoloAnnotator/
├── backend/                    # 后端服务
│   ├── main.py                # FastAPI 主应用
│   ├── api_extensions.py      # API 扩展功能
│   ├── export_utils.py        # 格式导出工具
│   ├── start.py               # 启动脚本
│   ├── requirements.txt       # Python 依赖
│   ├── images/                # 图片存储目录
│   ├── annotations/           # 标注文件目录
│   ├── models/                # 训练模型目录
│   └── datasets/              # 训练数据集目录
│       ├── train/             # 训练集
│       └── val/               # 验证集
├── MainWindow.xaml(.cs)       # 主窗口 - 图像标注
├── DetectionWindow.xaml(.cs)  # 检测窗口
├── AutoAnnotationWindow.xaml(.cs)  # 自动标注窗口
├── CameraRealtimeWindow.xaml(.cs)  # 摄像头实时检测
├── AdvancedDetectionWindow.xaml(.cs)  # 高级检测
├── ColorSelectionWindow.xaml(.cs)  # 颜色选择
├── ClassSelectDialog.xaml(.cs)  # 类别选择
├── ExportDialog.xaml(.cs)     # 导出对话框
├── Models/                    # 数据模型
│   ├── AnnotationData.cs      # 标注数据模型
│   └── Shape.cs               # 形状模型
├── Converters/                # WPF 转换器
└── YoloAnnotator.csproj       # 项目配置文件
```

## 安装指南

### 前置要求

- **Windows 10/11** (WPF 应用)
- **.NET 10.0 SDK** 或更高版本
- **Python 3.8+**
- **NVIDIA GPU** (可选，用于 GPU 加速训练)
- **CUDA 11.8/12.1/12.4** (可选，用于 GPU 支持)

### 后端安装

1. **安装 Python 依赖**

```bash
cd backend
pip install -r requirements.txt
```

2. **安装 PyTorch (根据您的 CUDA 版本)**

```bash
# CPU 版本
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu

# CUDA 11.8
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118

# CUDA 12.1
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121

# CUDA 12.4
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu124
```

3. **启动后端服务**

```bash
cd backend
python start.py
```

后端服务将在 `http://localhost:8000` 启动。

### 前端安装

1. **安装 .NET SDK**

从 [Microsoft 官网](https://dotnet.microsoft.com/download) 下载并安装 .NET 10.0 SDK。

2. **构建应用程序**

```bash
dotnet build
```

3. **运行应用程序**

```bash
dotnet run
```

或者在 Visual Studio 中打开 `YoloAnnotator.csproj` 并运行。

## 使用指南

### 1. 图像标注

1. **上传图片**
   - 点击"上传图片"按钮
   - 选择要标注的图片
   - 图片将显示在左侧列表中

2. **选择标注工具**
   - 矩形框：用于目标检测
   - 多边形：用于实例分割
   - 旋转框：用于旋转目标检测
   - 关键点：用于姿态估计
   - 其他工具：圆形、线段、点

3. **标注图片**
   - 在图片上绘制标注
   - 为每个标注分配类别
   - 调整标注框大小和位置
   - 保存标注

4. **快捷键**
   - `Ctrl+S`: 保存标注
   - `Ctrl+Z`: 撤销
   - `Delete`: 删除选中的标注
   - `Ctrl+C/V`: 复制/粘贴标注
   - `方向键`: 移动选中的标注

### 2. AI 辅助标注

1. **自动标注**
   - 打开"自动标注"窗口
   - 选择预训练模型
   - 设置置信度阈值
   - 点击"开始标注"

2. **批量标注**
   - 选择多张图片
   - 使用批量 AI 标注功能
   - 自动为所有图片生成标注

### 3. 模型训练

1. **准备数据**
   - 确保已标注足够的图片（建议每类至少 100 张）
   - 标注数据会自动分为训练集和验证集（80:20）

2. **配置训练参数**
   - 选择模型类型（YOLOv8n/s/m/l/x 或 YOLO26）
   - 设置训练轮数（epochs）
   - 设置批次大小（batch size）
   - 配置数据增强参数
   - 选择设备（CPU/GPU）

3. **开始训练**
   - 点击"开始训练"
   - 训练过程在后台运行
   - 训练完成后模型保存在 `backend/models/` 目录

4. **训练参数说明**
   - `epochs`: 训练轮数，建议 100-300
   - `batch_size`: 批次大小，根据 GPU 内存调整
   - `image_size`: 输入图像大小，默认 640
   - `patience`: 早停耐心值，默认 50
   - `lr0`: 初始学习率，默认 0.01
   - 数据增强：hsv、旋转、翻转、mosaic 等

### 4. 目标检测

1. **图像检测**
   - 打开"检测"窗口
   - 选择训练好的模型
   - 上传待检测图片
   - 查看检测结果

2. **视频检测**
   - 打开"视频检测"窗口
   - 选择视频文件
   - 实时显示检测结果

3. **摄像头检测**
   - 打开"摄像头实时检测"窗口
   - 选择摄像头设备
   - 实时检测和显示结果

### 5. 导出标注

1. **选择导出格式**
   - YOLO 格式（.txt）

2. **导出数据**
   - 打开"导出"对话框
   - 选择导出格式和目录
   - 点击"导出"

## API 文档

后端服务提供了完整的 RESTful API，启动后访问 `http://localhost:8000/docs` 查看 Swagger 文档。

### 主要 API 端点

- `GET /api/images` - 获取图片列表
- `POST /api/images/upload` - 上传图片
- `GET /api/images/{image_name}` - 获取图片
- `GET /api/annotations/{image_name}` - 获取标注
- `POST /api/annotations/{image_name}` - 保存标注
- `POST /api/train` - 开始训练
- `GET /api/detect` - 目标检测
- `POST /api/ai/annotate` - AI 辅助标注
- `POST /api/export` - 导出标注
- `GET /api/check-cuda` - 检查 CUDA 状态

## 配置文件

### classes.json
存储类别列表，格式：
```json
["person", "car", "dog", "cat"]
```

### color_map.json
存储类别颜色映射，格式：
```json
{
  "person": "#ef4444",
  "car": "#22c55e"
}
```

### data.yaml
YOLO 训练配置文件，自动生成：
```yaml
path: /path/to/datasets
train: train/images
val: val/images
nc: 4
names: {0: 'person', 1: 'car', 2: 'dog', 3: 'cat'}
task: detect
```

## 常见问题

### 1. GPU 未检测到

**问题**: 应用显示"GPU: 未检测到CUDA"

**解决方案**:
- 确认已安装 NVIDIA 显卡驱动
- 安装对应版本的 CUDA Toolkit
- 安装支持 CUDA 的 PyTorch 版本
- 检查 PyTorch 是否正确识别 GPU：
  ```python
  import torch
  print(torch.cuda.is_available())
  ```

### 2. 后端连接失败

**问题**: 前端无法连接到后端服务

**解决方案**:
- 确认后端服务已启动（`python backend/start.py`）
- 检查端口 8000 是否被占用
- 检查防火墙设置
- 查看后端日志输出

### 3. 训练失败

**问题**: 模型训练过程中出错

**解决方案**:
- 确保至少有 2 张已标注的图片
- 检查标注数据是否完整
- 降低 batch_size（如果内存不足）
- 检查 GPU 内存是否充足
- 查看后端日志获取详细错误信息

### 4. 中文类别名称问题

**问题**: 中文类别名称显示或保存异常

**解决方案**:
- 项目已支持 UTF-8 编码
- 确保所有 JSON 文件使用 UTF-8 编码
- 标注文件会自动使用 UTF-8 保存

## 性能优化建议

1. **GPU 加速**
   - 使用 NVIDIA GPU 可大幅提升训练和推理速度
   - 推荐 8GB+ 显存用于训练

2. **批次大小**
   - GPU: 根据显存调整（8GB 显存建议 batch_size=16）
   - CPU: 建议 batch_size=4-8

3. **图像大小**
   - 默认 640x640 适合大多数场景
   - 小目标检测可使用 1280x1280
   - 实时检测可使用 416x416

4. **数据增强**
   - 训练数据较少时启用更多增强
   - 训练数据充足时可适当减少增强

## 开发计划

- [ ] 支持更多 YOLO 模型（YOLOv9, YOLOv10）
- [ ] 添加模型对比功能
- [ ] 支持视频标注
- [ ] 添加数据集管理功能
- [ ] 支持分布式训练
- [ ] 添加模型量化和优化
- [ ] 支持更多导出格式

## 贡献指南

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 提交 Pull Request

## 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

MIT 许可证允许您：
- ✅ 商业使用
- ✅ 修改代码
- ✅ 分发
- ✅ 私人使用

唯一要求是在所有副本中包含版权声明和许可证声明。

## 联系方式

- 项目地址：[https://github.com/YDERO3452/YoloAnnotator](https://github.com/YDERO3452/YoloAnnotator)
- 问题反馈：[Issues](https://github.com/YDERO3452/YoloAnnotator/issues)

如有问题或建议，欢迎提交 Issue 或 Pull Request。

---

**注意**: 本项目仅供学习和研究使用，请遵守相关法律法规。
