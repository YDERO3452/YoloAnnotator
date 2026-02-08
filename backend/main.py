from fastapi import FastAPI, File, UploadFile, HTTPException, BackgroundTasks, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field, AliasChoices
from typing import List, Optional, Dict, Any
import os
import json
import base64
import cv2
import numpy as np
from PIL import Image
import io
from pathlib import Path
import httpx
import asyncio
import time
from ultralytics import YOLO

# 导入模型管理器
# from models.model_manager import model_manager
# 注意：model_manager 模块暂未实现，相关功能将被禁用

# ==================== 目录结构 ====================
BASE_DIR = Path(__file__).parent
IMAGES_DIR = BASE_DIR / "images"
ANNOTATIONS_DIR = BASE_DIR / "annotations"
MODELS_DIR = BASE_DIR / "models"
DATASETS_DIR = BASE_DIR / "datasets"

# 导入 API 扩展
import api_extensions

app = FastAPI(title="YOLO Annotator Backend")

# 添加请求日志中间件
@app.middleware("http")
async def log_requests(request, call_next):
    # 只对 POST 请求打印请求体
    if request.method == "POST":
        body = await request.body()
        if body:
            print(f"[DEBUG] {request.method} {request.url.path}")
            try:
                import json
                print(f"[DEBUG] 请求体: {body.decode('utf-8')[:500]}")  # 只打印前500字符
            except:
                print(f"[DEBUG] 请求体 (无法解码): {len(body)} bytes")
    response = await call_next(request)
    return response

# 允许跨域
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# ==================== 注册 API 扩展端点 ====================

# 模型管理 API
app.get("/api/models")(api_extensions.get_available_models)
app.get("/api/models/{model_name}/info")(api_extensions.get_model_info)
app.post("/api/models/{model_name}/load")(api_extensions.load_model_api)
app.post("/api/models/{model_name}/unload")(api_extensions.unload_model_api)
app.post("/api/models/preload")(api_extensions.preload_models_api)
app.post("/api/models/unload-all")(api_extensions.unload_all_models_api)

# 批量推理 API
app.post("/api/batch/detect")(api_extensions.batch_detect)

# AI 标注 API
app.post("/api/ai/annotate")(api_extensions.ai_annotate)

# OCR 识别 API
app.post("/api/ocr")(api_extensions.ocr_detect)

# SAM 分割 API
app.post("/api/sam/segment")(api_extensions.sam_segment)

# RT-DETR 检测 API
app.post("/api/rtdetr/detect")(api_extensions.rtdetr_detect)

# 格式导出 API
app.post("/api/export")(api_extensions.export_annotations)

# 图像分类 API
app.post("/api/classify")(api_extensions.classify_image)

# 跟踪 API
app.post("/api/track")(api_extensions.track_objects)

# GroundingDINO 文本提示检测 API
app.post("/api/grounding-dino/detect")(api_extensions.grounding_dino_detect)
app.post("/api/grounding-dino/batch-detect")(api_extensions.batch_grounding_dino_detect)






# 全局模型变量

current_model = None

pose_model = None  # 姿态估计模型

segmentation_model = None  # 分割模型

license_plate_model = None  # 车牌检测模型

# 创建必要目录
for dir_path in [IMAGES_DIR, ANNOTATIONS_DIR, MODELS_DIR, DATASETS_DIR]:
    dir_path.mkdir(exist_ok=True)

# 全局变量
current_model = None

# ==================== 健康检查 ====================

@app.get("/health")
async def health_check():
    """健康检查端点"""
    return {
        "status": "ok",
        "service": "YOLO Annotator Backend",
        "version": "1.0.0"
    }

@app.get("/api/health")
async def api_health_check():
    """API 健康检查端点"""
    return {
        "status": "ok",
        "service": "YOLO Annotator Backend",
        "version": "1.0.0"
    }

# ==================== 数据模型 ====================

class BoundingBox(BaseModel):
    x: float  # 中心点 x 坐标（相对于图片宽度 0-1）
    y: float  # 中心点 y 坐标（相对于图片高度 0-1）
    width: float  # 宽度（相对于图片宽度 0-1）
    height: float  # 高度（相对于图片高度 0-1）
    class_id: int = Field(validation_alias=AliasChoices('classId', 'class_id'), serialization_alias='class_id')
    class_name: str = Field(default="", validation_alias=AliasChoices('className', 'class_name'), serialization_alias='class_name')
    confidence: float = 0.0  # 置信度
    annotation_type: str = "bbox"  # 标注类型: bbox, obb, polygon, keypoints
    angle: float = 0.0  # 旋转角度（弧度，仅 OBB 使用）
    points: List[List[float]] = []  # 多边形顶点列表 [[x1, y1], [x2, y2], ...]（仅分割使用）
    keypoints: List[List[float]] = []  # 关键点列表 [[x1, y1], [x2, y2], ...]（仅姿态估计使用）
    color: Optional[str] = None  # 自定义颜色（十六进制格式）

class Annotation(BaseModel):
    image_name: str = Field(validation_alias=AliasChoices('imageName', 'image_name'), serialization_alias='image_name')
    width: int
    height: int
    bboxes: List[BoundingBox]

class TrainRequest(BaseModel):
    # 基础训练参数
    epochs: int = 100
    batch_size: int = 16
    image_size: int = 640
    device: str = "0"  # GPU 设备 ID
    classes: List[str] = []  # 添加所有类别列表
    model_type: str = "yolo26n"  # 模型类型
    weights_path: str = ""  # 预训练权重路径
    task_type: str = "detection"  # 任务类型: detection, segmentation, obb
    
    # 早停机制参数
    patience: int = 50  # 早停耐心值，多少个 epoch 没有改善就停止训练
    
    # 数据增强参数（根据 YOLO 官方文档）
    hsv_h: float = 0.015  # 色调增强
    hsv_s: float = 0.7    # 饱和度增强
    hsv_v: float = 0.4    # 明度增强
    degrees: float = 0.0  # 旋转角度
    translate: float = 0.1  # 平移
    scale: float = 0.5    # 缩放
    shear: float = 0.0    # 剪切
    perspective: float = 0.0  # 透视变换
    flipud: float = 0.0   # 上下翻转概率
    fliplr: float = 0.5   # 左右翻转概率
    mosaic: float = 1.0   # Mosaic 增强概率
    mixup: float = 0.0    # Mixup 增强概率
    copy_paste: float = 0.0  # Copy-Paste 增强概率（仅分割任务）
    
    # 学习率参数
    lr0: float = 0.01     # 初始学习率
    lrf: float = 0.01     # 最终学习率（相对于初始学习率的比例）
    momentum: float = 0.937  # SGD 动量
    weight_decay: float = 0.0005  # 权重衰减
    warmup_epochs: float = 3.0  # 学习率预热轮数
    
    # 其他训练参数
    cos_lr: bool = False  # 是否使用余弦学习率调度
    close_mosaic: int = 10  # 在最后 N 个 epoch 关闭 mosaic 增强
    amp: bool = True      # 自动混合精度训练
    fraction: float = 1.0  # 使用数据集的比例
    
    # 损失权重
    box: float = 7.5      # 边界框损失权重
    cls: float = 0.5      # 分类损失权重
    dfl: float = 1.5      # 分布焦点损失权重

class DetectionResult(BaseModel):
    bboxes: List[BoundingBox]
    classes: List[str]
    confidences: List[float]

# ==================== API 路由 ====================

@app.get("/")
async def root():
    return {"message": "YOLO Annotator Backend API", "version": "1.0.0"}

@app.get("/api/images")
async def list_images():
    """获取所有图片列表"""
    images = []
    for ext in ['*.jpg', '*.jpeg', '*.png', '*.bmp']:
        images.extend(IMAGES_DIR.glob(ext))
    return {"images": [img.name for img in images]}

@app.post("/api/images/upload")
async def upload_image(file: UploadFile = File(...)):
    """上传图片"""
    print(f"[DEBUG] 收到上传请求, 原始文件名: {file.filename}, content_type: {file.content_type}")

    try:
        # 安全的文件名处理
        filename = file.filename

        # 解码 MIME 编码的文件名 (RFC 2047)
        if filename.startswith("=?"):
            try:
                import email.header
                decoded = email.header.decode_header(filename)
                filename = decoded[0][0]
                if isinstance(filename, bytes):
                    filename = filename.decode('utf-8')
                print(f"[DEBUG] 解码后的文件名: {filename}")
            except Exception as e:
                print(f"[DEBUG] 解码文件名失败，使用原始文件名: {e}")

        if not filename:
            print("[DEBUG] 文件名为空")
            raise HTTPException(status_code=400, detail="文件名为空")

        # 使用文件扩展名验证
        allowed_extensions = {'.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp'}
        file_ext = Path(filename).suffix.lower()

        print(f"[DEBUG] 文件扩展名: {file_ext}, 允许的扩展名: {allowed_extensions}")

        if file_ext not in allowed_extensions:
            print(f"[DEBUG] 不支持的文件扩展名: {file_ext}")
            raise HTTPException(status_code=400, detail=f"只支持图片文件格式: {', '.join(allowed_extensions)}")

        print(f"[DEBUG] 文件名验证通过: {filename}, 扩展名: {file_ext}")

        # 保存图片
        file_path = IMAGES_DIR / filename
        print(f"保存路径: {file_path}")

        content = await file.read()
        print(f"读取到 {len(content)} 字节")

        if len(content) == 0:
            raise HTTPException(status_code=400, detail="文件内容为空")

        with file_path.open("wb") as f:
            f.write(content)

        print(f"文件保存成功: {filename}")
        return {"message": "上传成功", "filename": filename}
    except HTTPException:
        raise
    except Exception as e:
        import traceback
        error_detail = f"上传失败: {str(e)}\n{traceback.format_exc()}"
        print(error_detail)
        raise HTTPException(status_code=500, detail=error_detail)

@app.get("/api/images/{image_name}")
async def get_image(image_name: str):
    """获取图片（base64 编码）"""
    file_path = IMAGES_DIR / image_name
    if not file_path.exists():
        raise HTTPException(status_code=404, detail="图片不存在")

    with file_path.open("rb") as f:
        img_data = base64.b64encode(f.read()).decode()

    return {"image": img_data, "name": image_name}

@app.delete("/api/images/{image_name}")
async def delete_image(image_name: str):
    """删除图片"""
    file_path = IMAGES_DIR / image_name
    annotation_path = ANNOTATIONS_DIR / f"{image_name}.json"

    if file_path.exists():
        file_path.unlink()
    if annotation_path.exists():
        annotation_path.unlink()

    return {"message": "删除成功"}

@app.get("/api/annotations/{image_name}")
async def get_annotation(image_name: str):
    """获取图片的标注"""
    annotation_path = ANNOTATIONS_DIR / f"{image_name}.json"
    if not annotation_path.exists():
        return {"bboxes": []}

    with annotation_path.open("r", encoding='utf-8') as f:
        data = json.load(f)

    print(f"[DEBUG] 加载到的标注数据:")
    print(json.dumps(data, ensure_ascii=False, indent=2)[:1000])  # 只打印前 1000 个字符
    
    return data

@app.post("/api/annotations/{image_name}")
async def save_annotation(image_name: str, annotation: Annotation):
    """保存标注"""
    try:
        print(f"[DEBUG] ========== 保存标注开始 ==========")
        print(f"[DEBUG] 图片={image_name}, 边界框数量={len(annotation.bboxes)}")
        print(f"[DEBUG] 接收到的标注数据: 宽度={annotation.width}, 高度={annotation.height}")
        print(f"[DEBUG] 边界框详情:")
        for i, bbox in enumerate(annotation.bboxes):
            print(f"  [{i}] class_id={bbox.class_id}, class_name='{bbox.class_name}', x={bbox.x}, y={bbox.y}")
            print(f"       annotation_type={bbox.annotation_type}, confidence={bbox.confidence}")

        # 过滤掉类别名称为空的标注框
        valid_bboxes = [bbox for bbox in annotation.bboxes if bbox.class_name and bbox.class_name.strip()]
        if len(valid_bboxes) != len(annotation.bboxes):
            print(f"[WARNING] 过滤掉了 {len(annotation.bboxes) - len(valid_bboxes)} 个类别名称为空的标注框")
            print(f"[DEBUG] 过滤后的标注框数量: {len(valid_bboxes)}")

        annotation_path = ANNOTATIONS_DIR / f"{image_name}.json"

        # 确保目录存在
        ANNOTATIONS_DIR.mkdir(parents=True, exist_ok=True)

        # 使用 Pydantic 的 model_dump_json() 方法序列化
        json_str = annotation.model_dump_json(indent=2, ensure_ascii=False)
        
        print(f"[DEBUG] 即将保存的 JSON 数据 (前 1500 字符):")
        print(json_str[:1500])
        
        with annotation_path.open("w", encoding='utf-8') as f:
            f.write(json_str)

        print(f"[DEBUG] 标注保存成功: {annotation_path}")
        print(f"[DEBUG] ========== 保存标注结束 ==========")
        return {"message": "保存成功"}
    except Exception as e:
        import traceback
        error_detail = f"保存标注失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

@app.get("/api/check-cuda")
async def check_cuda():
    """检查CUDA是否可用，并提供详细的诊断信息"""
    try:
        import torch
        import platform
        
        # PyTorch 版本信息
        pytorch_version = torch.__version__
        pytorch_cuda_version = torch.version.cuda if hasattr(torch.version, "cuda") else None
        
        # 检查 CUDA 是否可用
        available = torch.cuda.is_available()
        device_name = ""
        device_count = 0
        driver_version = ""
        compute_capability = ""
        gpu_memory = 0
        
        # 检查系统信息
        system_info = platform.system()
        python_version = platform.python_version()
        
        if available:
            device_count = torch.cuda.device_count()
            device_name = torch.cuda.get_device_name(0)
            driver_version = torch.cuda.driver_version
            compute_capability = torch.cuda.get_device_capability(0)
            # 获取GPU内存信息
            gpu_memory = torch.cuda.get_device_properties(0).total_memory / 1024**3  # GB
            
            return {
                "available": True,
                "pytorch_version": pytorch_version,
                "cuda_version": pytorch_cuda_version,
                "device_name": device_name,
                "device_count": device_count,
                "gpu_memory_gb": round(gpu_memory, 2),
                "driver_version": driver_version,
                "compute_capability": compute_capability,
                "system": system_info,
                "python_version": python_version
            }
        else:
            # 如果CUDA不可用，提供详细的原因和诊断信息
            reasons = []
            
            # 检查 PyTorch 是否是 CPU 版本
            if "cpu" in pytorch_version.lower() or pytorch_cuda_version is None:
                reasons.append("当前安装的是 CPU 版本的 PyTorch，不支持 CUDA")
                reasons.append(f"当前 PyTorch 版本: {pytorch_version}")
                reasons.append("解决方法: 重新安装支持 CUDA 的 PyTorch")
                reasons.append("  Windows: pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118")
                reasons.append("  Linux: pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118")
            else:
                reasons.append(f"PyTorch 支持 CUDA {pytorch_cuda_version}，但 CUDA 不可用")
                reasons.append("可能的原因:")
                reasons.append("  1. 未安装 NVIDIA 显卡驱动")
                reasons.append("  2. NVIDIA 驱动版本过低")
                reasons.append("  3. CUDA 运行时未安装或版本不匹配")
                reasons.append("  4. 显卡不支持 CUDA")
            
            return {
                "available": False,
                "pytorch_version": pytorch_version,
                "cuda_version": pytorch_cuda_version,
                "device_name": None,
                "device_count": 0,
                "gpu_memory_gb": 0,
                "driver_version": None,
                "compute_capability": None,
                "system": system_info,
                "python_version": python_version,
                "reason": "; ".join(reasons)
            }
    except Exception as e:
        import traceback
        print(f"[ERROR] 检查CUDA失败: {e}")
        print(f"[ERROR] 详细错误: {traceback.format_exc()}")
        return {
            "available": False,
            "pytorch_version": "Unknown",
            "cuda_version": None,
            "device_name": None,
            "device_count": 0,
            "gpu_memory_gb": 0,
            "driver_version": None,
            "compute_capability": None,
            "system": "Unknown",
            "python_version": "Unknown",
            "reason": f"检查失败: {str(e)}"
        }

@app.post("/api/train")
async def train_model(request: TrainRequest, background_tasks: BackgroundTasks):
    """开始训练模型"""
    # 检查是否有标注数据
    annotation_files = list(ANNOTATIONS_DIR.glob("*.json"))
    if not annotation_files:
        raise HTTPException(status_code=400, detail="没有标注数据，请先标注图片")

    # 如果只有 1 个标注文件，给用户警告
    if len(annotation_files) == 1:
        print("[WARNING] 只有 1 个标注文件，将全部用于训练，没有验证集。建议至少标注 2 张图片。")

    # 准备 YOLO 数据集格式（传入所有类别和任务类型）
    dataset_path = prepare_yolo_dataset(request.classes, request.task_type)

    # 开始训练（后台任务）- 传入完整的 request 对象
    background_tasks.add_task(run_training, dataset_path, request)

    return {"message": "训练已开始", "dataset_path": str(dataset_path)}

def prepare_yolo_dataset(all_classes: List[str] = None, task_type: str = "detection"):
    """准备 YOLO 格式的数据集"""
    print(f"[DEBUG] 开始准备 YOLO 数据集，任务类型: {task_type}...")

    # 创建数据集目录
    train_images = DATASETS_DIR / "train" / "images"
    train_labels = DATASETS_DIR / "train" / "labels"
    val_images = DATASETS_DIR / "val" / "images"
    val_labels = DATASETS_DIR / "val" / "labels"

    for path in [train_images, train_labels, val_images, val_labels]:
        path.mkdir(parents=True, exist_ok=True)

    # 复制图片和标签（简单起见，80% 训练，20% 验证）
    annotations = list(ANNOTATIONS_DIR.glob("*.json"))
    if not annotations:
        print("[WARNING] 没有找到标注文件")
        return DATASETS_DIR

    # 如果只有 1 个标注文件，全部放入训练集
    if len(annotations) == 1:
        train_count = 1
    else:
        train_count = int(len(annotations) * 0.8)
    print(f"[DEBUG] 找到 {len(annotations)} 个标注文件，训练集: {train_count}, 验证集: {len(annotations) - train_count}")

    # 强制从标注中提取类别（支持中文等非英文类别）
    # 收集所有类别
    class_names = set()
    # 保存原始标注数据，用于后续重新映射
    original_annotations = {}

    for i, ann_file in enumerate(annotations):
        image_name = ann_file.stem
        image_path = IMAGES_DIR / image_name
        if not image_path.exists():
            print(f"[WARNING] 图片不存在: {image_path}")
            continue

        with ann_file.open("r", encoding='utf-8') as f:
            ann_data = json.load(f)

        # 保存原始标注
        original_annotations[image_name] = ann_data.get("bboxes", [])

        # 收集类别名称
        for bbox in ann_data.get("bboxes", []):
            class_name = bbox.get("class_name", "").strip()
            # 过滤掉空字符串和空白字符
            if class_name:
                class_names.add(class_name)

    # 按类别名称排序，创建类别映射
    sorted_classes = sorted([name for name in class_names if name.strip()])
    class_to_id = {name: idx for idx, name in enumerate(sorted_classes)}
    num_classes = len(sorted_classes)
    print(f"[DEBUG] 从标注提取的类别: {sorted_classes}")
    print(f"[DEBUG] 类别映射: {class_to_id}")
    print(f"[DEBUG] 类别数量: {num_classes}")

    # 使用新的类别映射转换标注
    for i, ann_file in enumerate(annotations):
        image_name = ann_file.stem
        if image_name not in original_annotations:
            continue

        image_path = IMAGES_DIR / image_name
        if not image_path.exists():
            continue

        # 转换标注格式为 YOLO 格式，使用新的 class_id
        yolo_labels = []
        for bbox in original_annotations[image_name]:
            class_name = bbox.get("class_name", f"class_{bbox['class_id']}")
            new_class_id = class_to_id.get(class_name, 0)
            
            # 检查 class_id 是否超出范围
            if new_class_id >= num_classes:
                print(f"[WARNING] 类别 '{class_name}' 的 ID {new_class_id} 超出范围，将使用 0")
                new_class_id = 0
            
            # 根据任务类型生成不同的标签格式
            if task_type == "segmentation" and bbox.get("annotation_type") == "polygon":
                # 分割任务：多边形格式
                # YOLO 分割格式: class_id x1 y1 x2 y2 ... xn yn (归一化坐标)
                points = bbox.get("points", [])
                if len(points) >= 3:
                    points_str = " ".join([f"{p[0]:.6f} {p[1]:.6f}" for p in points])
                    yolo_labels.append(f"{new_class_id} {points_str}")
                else:
                    # 多边形点数不足，跳过
                    print(f"[WARNING] 多边形点数不足: {len(points)}")
            elif task_type == "obb" and bbox.get("annotation_type") == "obb":
                # OBB 任务：旋转框格式
                # YOLO OBB 格式: class_id x_center y_center width height angle (弧度)
                angle = bbox.get("angle", 0.0)
                yolo_labels.append(f"{new_class_id} {bbox['x']:.6f} {bbox['y']:.6f} {bbox['width']:.6f} {bbox['height']:.6f} {angle:.6f}")
            else:
                # 检测任务（默认）：边界框格式
                yolo_labels.append(f"{new_class_id} {bbox['x']:.6f} {bbox['y']:.6f} {bbox['width']:.6f} {bbox['height']:.6f}")

        # 复制到训练集或验证集
        if i < train_count:
            dest_img = train_images / f"{image_name}{image_path.suffix}"
            dest_lbl = train_labels / f"{image_name}.txt"
        else:
            dest_img = val_images / f"{image_name}{image_path.suffix}"
            dest_lbl = val_labels / f"{image_name}.txt"

        import shutil
        shutil.copy(image_path, dest_img)
        dest_lbl.write_text("\n".join(yolo_labels))

    # 检查哪些类别没有标注数据
    used_classes = set()
    for bbox_list in original_annotations.values():
        for bbox in bbox_list:
            used_classes.add(bbox.get("class_name", ""))
    
    unused_classes = set(sorted_classes) - used_classes
    if unused_classes:
        print(f"[WARNING] 以下类别没有标注数据: {sorted(unused_classes)}")
        print(f"[WARNING] 模型将无法检测这些类别，建议为每个类别至少标注 5-10 张图片")

    # 检查验证集是否为空
    val_has_images = len(list(val_images.glob("*.jpg"))) + len(list(val_images.glob("*.jpeg"))) + len(list(val_images.glob("*.png"))) > 0

    # 创建 data.yaml
    data_yaml = DATASETS_DIR / "data.yaml"
    # 构建 YAML 格式的类别映射
    names_dict = {i: name for i, name in enumerate(sorted_classes)}
    
    # 根据任务类型设置不同的 YAML 配置
    task_key = "segment" if task_type == "segmentation" else ("obb" if task_type == "obb" else "detect")
    
    if val_has_images:
        # 有验证集，正常配置
        yaml_content = f"""path: {DATASETS_DIR.absolute()}
train: train/images
val: val/images
nc: {num_classes}
names: {names_dict}
task: {task_key}
"""
        print(f"[DEBUG] 验证集非空，使用正常配置")
    else:
        # 没有验证集，val 指向训练集
        yaml_content = f"""path: {DATASETS_DIR.absolute()}
train: train/images
val: train/images
nc: {num_classes}
names: {names_dict}
task: {task_key}
"""
        print(f"[WARNING] 验证集为空，将训练集作为验证集（不推荐用于实际生产环境）")

    data_yaml.write_text(yaml_content, encoding='utf-8')
    print(f"[DEBUG] data.yaml 已创建: {data_yaml}")
    print(f"[DEBUG] data.yaml 内容:\n{yaml_content}")

    return DATASETS_DIR

def run_training(dataset_path: Path, request: TrainRequest):
    """
    执行训练
    
    Args:
        dataset_path: 数据集路径
        request: 训练请求对象，包含所有训练参数
    """
    try:
        # 检查 CUDA 是否可用
        import torch
        
        # 转换设备参数
        if request.device == "cpu":
            device_param = "cpu"
            print(f"[DEBUG] 使用 CPU 训练")
        else:
            # device 应该是 "0" 或类似 "cuda:0"
            # 尝试转换为 CUDA 格式
            if request.device == "0":
                device_param = 0  # 整数0表示第一个GPU
            elif request.device.startswith("cuda:"):
                device_param = request.device
            else:
                # 尝试转换为整数
                try:
                    device_param = int(request.device)
                except ValueError:
                    device_param = 0  # 默认使用第一个GPU
            
            # 验证 CUDA 是否可用
            if not torch.cuda.is_available():
                print(f"[WARNING] CUDA 不可用，自动切换到 CPU 训练")
                device_param = "cpu"
            else:
                print(f"[DEBUG] CUDA 可用，使用设备: {device_param} (类型: {type(device_param)})")
                # 显示 GPU 信息
                print(f"[DEBUG] GPU 设备名称: {torch.cuda.get_device_name(0)}")
                print(f"[DEBUG] GPU 数量: {torch.cuda.device_count()}")

        print(f"[DEBUG] 开始训练模型...")
        print(f"[DEBUG] 数据集路径: {dataset_path}")
        print(f"[DEBUG] 训练参数:")
        print(f"  - epochs={request.epochs}, batch={request.batch_size}, imgsz={request.image_size}")
        print(f"  - device={device_param}, model_type={request.model_type}, task_type={request.task_type}")
        print(f"  - patience={request.patience} (早停)")
        print(f"  - lr0={request.lr0}, lrf={request.lrf}, momentum={request.momentum}")
        print(f"  - 数据增强: hsv_h={request.hsv_h}, hsv_s={request.hsv_s}, hsv_v={request.hsv_v}")
        print(f"  - 数据增强: degrees={request.degrees}, translate={request.translate}, scale={request.scale}")
        print(f"  - 数据增强: fliplr={request.fliplr}, mosaic={request.mosaic}, mixup={request.mixup}")

        # 根据任务类型确定模型文件名
        if request.weights_path:
            model_name = request.weights_path
        else:
            # 根据任务类型选择对应的模型
            model_base = request.model_type
            if request.task_type == "segmentation":
                model_name = f"{model_base}-seg.pt"
            elif request.task_type == "obb":
                model_name = f"{model_base}-obb.pt"
            else:
                model_name = f"{model_base}.pt"
            print(f"[DEBUG] 使用默认模型: {model_name}")

        print(f"[DEBUG] 加载模型: {model_name}")
        model = YOLO(model_name)

        # 开始训练 - 使用完整的参数配置
        print(f"[DEBUG] 开始训练...")
        results = model.train(
            # 基础参数
            data=str(dataset_path / "data.yaml"),
            epochs=request.epochs,
            batch=request.batch_size,
            imgsz=request.image_size,
            device=device_param,
            project=str(MODELS_DIR),
            name='yolo_model',
            exist_ok=True,
            
            # 早停机制
            patience=request.patience,
            
            # 数据增强参数
            hsv_h=request.hsv_h,
            hsv_s=request.hsv_s,
            hsv_v=request.hsv_v,
            degrees=request.degrees,
            translate=request.translate,
            scale=request.scale,
            shear=request.shear,
            perspective=request.perspective,
            flipud=request.flipud,
            fliplr=request.fliplr,
            mosaic=request.mosaic,
            mixup=request.mixup,
            copy_paste=request.copy_paste if request.task_type == "segmentation" else 0.0,
            
            # 学习率参数
            lr0=request.lr0,
            lrf=request.lrf,
            momentum=request.momentum,
            weight_decay=request.weight_decay,
            warmup_epochs=request.warmup_epochs,
            
            # 其他训练参数
            cos_lr=request.cos_lr,
            close_mosaic=request.close_mosaic,
            amp=request.amp,
            fraction=request.fraction,
            
            # 损失权重
            box=request.box,
            cls=request.cls,
            dfl=request.dfl,
            
            # 保存和验证
            save=True,
            val=True,
            plots=True,
            verbose=True
        )

        print(f"[DEBUG] 训练完成！")
        print(f"[DEBUG] 模型保存在: {MODELS_DIR / 'yolo_model'}")
        print(f"[DEBUG] 最佳模型: {MODELS_DIR / 'yolo_model' / 'weights' / 'best.pt'}")
        
        # 打印训练结果摘要
        if results:
            print(f"[DEBUG] 训练结果摘要:")
            print(f"  - 最终 mAP50: {results.results_dict.get('metrics/mAP50(B)', 'N/A')}")
            print(f"  - 最终 mAP50-95: {results.results_dict.get('metrics/mAP50-95(B)', 'N/A')}")
        
        # 可选：导出为 ONNX 格式（如果需要）
        # try:
        #     print(f"[DEBUG] 开始导出模型为 ONNX 格式...")
        #     model.export(format='onnx')
        #     print(f"[DEBUG] 模型已导出为 ONNX 格式")
        # except Exception as export_error:
        #     print(f"[WARNING] ONNX 导出失败: {export_error}")
        #     print(f"[INFO] 可以继续使用 .pt 模型进行推理")

    except Exception as e:
        import traceback
        print(f"[ERROR] 训练失败: {e}")
        print(f"[ERROR] 详细错误:\n{traceback.format_exc()}")

@app.get("/api/model-classes")
async def get_model_classes():
    """获取模型使用的类别映射"""
    try:
        # 优先从 data.yaml 文件读取（训练后的类别）
        data_yaml = DATASETS_DIR / "data.yaml"
        if data_yaml.exists():
            import yaml
            with data_yaml.open("r", encoding='utf-8') as f:
                data = yaml.safe_load(f)
            
            names = data.get("names", {})
            print(f"[DEBUG] data.yaml 中的 names: {names}")
            
            # names 可能是字典 {0: 'cat', 1: 'dog'} 或列表 ['cat', 'dog']
            if isinstance(names, dict):
                # 如果是字典，按ID排序返回类别列表
                classes = [names[i] for i in sorted(names.keys())]
            elif isinstance(names, list):
                # 如果是列表，直接使用
                classes = names
            else:
                classes = []
            
            if classes:
                print(f"[DEBUG] 从 data.yaml 读取到 {len(classes)} 个类别: {classes}")
                return {"classes": classes}
        
        # 如果 data.yaml 不存在，尝试从标注文件中提取类别
        print(f"[DEBUG] data.yaml 不存在，尝试从标注文件提取类别")
        classes = set()
        for ann_file in ANNOTATIONS_DIR.glob("*.json"):
            try:
                with ann_file.open("r", encoding='utf-8') as f:
                    ann_data = json.load(f)
                for bbox in ann_data.get("bboxes", []):
                    class_name = bbox.get("class_name", "")
                    if class_name:
                        classes.add(class_name)
            except Exception as e:
                print(f"[WARNING] 读取标注文件失败 {ann_file}: {e}")
        
        if classes:
            # 按字母顺序排序，确保顺序一致
            sorted_classes = sorted(list(classes))
            print(f"[DEBUG] 从标注文件提取到 {len(sorted_classes)} 个类别: {sorted_classes}")
            return {"classes": sorted_classes}
        
        print(f"[WARNING] 没有找到任何类别信息")
        return {"classes": [], "error": "没有找到类别信息，请先标注图片或训练模型"}
    except Exception as e:
        import traceback
        print(f"[ERROR] 获取模型类别失败: {e}\n{traceback.format_exc()}")
        return {"classes": [], "error": str(e)}

@app.get("/api/detect")
async def detect_objects(image_name: str = Query(...), end2end: bool = True):
    """使用训练好的模型进行检测"""
    global current_model

    # 查找最新的模型文件（递归查找 weights 目录中的模型）
    model_files = list(MODELS_DIR.glob("**/best.onnx"))
    if not model_files:
        pt_files = list(MODELS_DIR.glob("**/best.pt"))
        if not pt_files:
            raise HTTPException(status_code=400, detail="没有训练好的模型，请先训练")
        model_path = pt_files[-1]
        print(f"[DEBUG] 使用 PyTorch 模型: {model_path}")
    else:
        model_path = model_files[-1]
        print(f"[DEBUG] 使用 ONNX 模型: {model_path}")

    # 加载模型（如果还没加载）
    if current_model is None:
        try:
            current_model = YOLO(str(model_path))
            print(f"[DEBUG] 模型加载成功")
        except Exception as e:
            print(f"[ERROR] 模型加载失败: {e}")
            raise HTTPException(status_code=500, detail=f"模型加载失败: {str(e)}")

    # 读取图片
    image_path = IMAGES_DIR / image_name
    if not image_path.exists():
        raise HTTPException(status_code=404, detail="图片不存在")

    print(f"[DEBUG] 开始检测图片: {image_path}, end2end={end2end}")

    # 获取模型类别映射
    model_classes = []
    try:
        data_yaml = DATASETS_DIR / "data.yaml"
        if data_yaml.exists():
            import yaml
            with data_yaml.open("r", encoding='utf-8') as f:
                data = yaml.safe_load(f)
            names = data.get("names", {})
            if isinstance(names, dict):
                model_classes = [names[i] for i in sorted(names.keys())]
            elif isinstance(names, list):
                model_classes = names
        print(f"[DEBUG] 模型类别: {model_classes}")
    except Exception as e:
        print(f"[WARNING] 获取模型类别失败: {e}")

    # 进行推理（设置 NMS 参数，根据 end2end 决定是否使用 NMS）
    # 官方默认值：conf=0.25, iou=0.7
    results = current_model(str(image_path), conf=0.25, iou=0.7)

    # 解析结果
    detections = []
    for result in results:
        # 检查是否是分割模型
        if hasattr(result, 'masks') and result.masks is not None:
            # 分割模型
            masks = result.masks
            boxes = result.boxes
            for i, box in enumerate(boxes):
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                x_center = (x1 + x2) / 2 / result.orig_shape[1]
                y_center = (y1 + y2) / 2 / result.orig_shape[0]
                width = (x2 - x1) / result.orig_shape[1]
                height = (y2 - y1) / result.orig_shape[0]
                class_id = int(box.cls[0])

                # 获取多边形点（归一化坐标）
                points = []
                if i < len(masks.xy):
                    mask_points = masks.xy[i].tolist()  # 原始像素坐标
                    for px, py in mask_points:
                        points.append([px / result.orig_shape[1], py / result.orig_shape[0]])

                # 根据 class_id 获取类别名称
                class_name = ""
                if 0 <= class_id < len(model_classes):
                    class_name = model_classes[class_id]

                detections.append({
                    "x": x_center,
                    "y": y_center,
                    "width": width,
                    "height": height,
                    "class_id": class_id,
                    "class_name": class_name,
                    "confidence": float(box.conf[0]),
                    "annotation_type": "polygon",
                    "points": points
                })
        elif hasattr(result, 'obb') and result.obb is not None:
            # OBB 模型
            obb_boxes = result.obb
            for box in obb_boxes:
                # OBB 格式: x_center, y_center, width, height, angle (弧度)
                x_center, y_center, width, height, angle = box.xywhr[0].tolist()
                class_id = int(box.cls[0])

                # 归一化坐标
                x_center = x_center / result.orig_shape[1]
                y_center = y_center / result.orig_shape[0]
                width = width / result.orig_shape[1]
                height = height / result.orig_shape[0]

                # 根据 class_id 获取类别名称
                class_name = ""
                if 0 <= class_id < len(model_classes):
                    class_name = model_classes[class_id]

                detections.append({
                    "x": x_center,
                    "y": y_center,
                    "width": width,
                    "height": height,
                    "class_id": class_id,
                    "class_name": class_name,
                    "confidence": float(box.conf[0]),
                    "annotation_type": "obb",
                    "angle": angle
                })
        else:
            # 检测模型
            boxes = result.boxes
            for box in boxes:
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                x_center = (x1 + x2) / 2 / result.orig_shape[1]
                y_center = (y1 + y2) / 2 / result.orig_shape[0]
                width = (x2 - x1) / result.orig_shape[1]
                height = (y2 - y1) / result.orig_shape[0]
                class_id = int(box.cls[0])

                # 根据 class_id 获取类别名称
                class_name = ""
                if 0 <= class_id < len(model_classes):
                    class_name = model_classes[class_id]

                detections.append({
                    "x": x_center,
                    "y": y_center,
                    "width": width,
                    "height": height,
                    "class_id": class_id,
                    "class_name": class_name,
                    "confidence": float(box.conf[0]),
                    "annotation_type": "bbox"
                })

    print(f"[DEBUG] 检测到 {len(detections)} 个物体")
    return {"detections": detections}

# ==================== 高级检测功能 ====================

class PoseDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    conf: float = Field(0.25, description="置信度阈值")

@app.post("/api/detect/pose")
async def detect_pose(request: PoseDetectRequest):
    """人体姿态估计"""
    global pose_model

    # 检查是否存在姿态模型
    pose_model_path = BASE_DIR / "yolov8n-pose.pt"
    if not pose_model_path.exists():
        # 尝试下载预训练模型
        try:
            print("[DEBUG] 正在下载 YOLOv8-pose 模型...")
            from ultralytics import YOLO
            pose_model = YOLO("yolov8n-pose.pt")
            pose_model.save(str(pose_model_path))
            print("[DEBUG] 姿态模型下载成功")
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"姿态模型加载失败: {str(e)}")

    # 加载模型
    if pose_model is None:
        from ultralytics import YOLO
        pose_model = YOLO(str(pose_model_path))

    # 读取图片
    image_path = IMAGES_DIR / request.image_name
    if not image_path.exists():
        raise HTTPException(status_code=404, detail="图片不存在")

    # 进行推理
    results = pose_model(str(image_path), conf=request.conf)

    # 解析姿态估计结果
    poses = []
    for result in results:
        if result.keypoints is not None:
            keypoints = result.keypoints
            for kp in keypoints.xy:
                # 归一化关键点坐标
                normalized_kp = []
                for i in range(len(kp)):
                    x = float(kp[i][0]) / result.orig_shape[1]
                    y = float(kp[i][1]) / result.orig_shape[0]
                    normalized_kp.append([x, y])
                poses.append({
                    "keypoints": normalized_kp,
                    "confidence": float(keypoints.conf[0]) if hasattr(keypoints, 'conf') else 1.0
                })

    return {"poses": poses, "count": len(poses)}

class SegmentDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    conf: float = Field(0.25, description="置信度阈值")

@app.post("/api/detect/segment")
async def detect_segmentation(request: SegmentDetectRequest):
    """人体分割/轮廓检测"""
    global segmentation_model

    # 检查是否存在分割模型
    seg_model_path = BASE_DIR / "yolov8n-seg.pt"
    if not seg_model_path.exists():
        try:
            print("[DEBUG] 正在下载 YOLOv8-seg 模型...")
            from ultralytics import YOLO
            segmentation_model = YOLO("yolov8n-seg.pt")
            segmentation_model.save(str(seg_model_path))
            print("[DEBUG] 分割模型下载成功")
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"分割模型加载失败: {str(e)}")

    # 加载模型
    if segmentation_model is None:
        from ultralytics import YOLO
        segmentation_model = YOLO(str(seg_model_path))

    # 读取图片
    image_path = IMAGES_DIR / request.image_name
    if not image_path.exists():
        raise HTTPException(status_code=404, detail="图片不存在")

    # 进行推理
    results = segmentation_model(str(image_path), conf=request.conf)

    # 解析分割结果
    segments = []
    for result in results:
        if result.masks is not None:
            masks = result.masks
            boxes = result.boxes
            for i, box in enumerate(boxes):
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                class_id = int(box.cls[0])
                confidence = float(box.conf[0])

                # 获取分割多边形点
                points = []
                if i < len(masks.xy):
                    mask_points = masks.xy[i].tolist()
                    for px, py in mask_points:
                        points.append([px / result.orig_shape[1], py / result.orig_shape[0]])

                segments.append({
                    "class_id": class_id,
                    "class_name": ["person", "car", "dog", "cat"][class_id] if class_id < 4 else f"class_{class_id}",
                    "confidence": confidence,
                    "bbox": {
                        "x": (x1 + x2) / 2 / result.orig_shape[1],
                        "y": (y1 + y2) / 2 / result.orig_shape[0],
                        "width": (x2 - x1) / result.orig_shape[1],
                        "height": (y2 - y1) / result.orig_shape[0]
                    },
                    "segmentation": points
                })

    return {"segments": segments, "count": len(segments)}

class LicensePlateRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    conf: float = Field(0.3, description="置信度阈值")

@app.post("/api/detect/license-plate")
async def detect_license_plate(request: LicensePlateRequest):
    """车牌检测"""
    global license_plate_model

    # 使用通用的 YOLOv8 模型检测车辆
    if current_model is None:
        model_files = list(MODELS_DIR.glob("**/best.onnx"))
        if not model_files:
            pt_files = list(MODELS_DIR.glob("**/best.pt"))
            if pt_files:
                current_model = YOLO(str(pt_files[-1]))
            else:
                # 使用预训练的 YOLOv8n 模型
                current_model = YOLO("yolov8n.pt")

    # 读取图片
    image_path = IMAGES_DIR / request.image_name
    if not image_path.exists():
        raise HTTPException(status_code=404, detail="图片不存在")

    # 进行推理
    results = current_model(str(image_path), conf=request.conf, classes=[2, 3, 5, 7])  # 检测车辆类别

    # 解析结果
    plates = []
    for result in results:
        boxes = result.boxes
        for box in boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            class_id = int(box.cls[0])
            confidence = float(box.conf[0])

            # 提取车牌区域（假设车牌在车辆下方）
            plate_y2 = min(y2 + (y2 - y1) * 0.3, result.orig_shape[0])

            plates.append({
                "vehicle": {
                    "class_id": class_id,
                    "class_name": ["car", "motorcycle", "bus", "truck"][class_id % 4],
                    "bbox": {
                        "x": (x1 + x2) / 2 / result.orig_shape[1],
                        "y": (y1 + y2) / 2 / result.orig_shape[0],
                        "width": (x2 - x1) / result.orig_shape[1],
                        "height": (y2 - y1) / result.orig_shape[0]
                    }
                },
                "confidence": confidence
            })

    return {"plates": plates, "count": len(plates)}

# ==================== OpenCV 更多功能 ====================

class FaceDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    scale_factor: float = Field(1.1, description="缩放因子")
    min_neighbors: int = Field(3, description="最小邻居数")

@app.post("/api/opencv/face-detect")
async def opencv_face_detect(request: FaceDetectRequest):
    """使用OpenCV Haar级联分类器进行人脸检测"""
    try:
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            raise HTTPException(status_code=404, detail="图片不存在")

        # 加载图片
        image = cv2.imread(str(image_path))
        if image is None:
            raise HTTPException(status_code=400, detail="图片读取失败")

        # 转换为灰度图
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

        # 加载Haar级联分类器
        face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
        eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

        # 检测人脸
        faces = face_cascade.detectMultiScale(
            gray,
            scaleFactor=request.scale_factor,
            minNeighbors=request.min_neighbors,
            minSize=(30, 30)
        )

        # 检测眼睛
        results = []
        for (x, y, w, h) in faces:
            # 归一化坐标
            face = {
                "x": (x + w / 2) / image.shape[1],
                "y": (y + h / 2) / image.shape[0],
                "width": w / image.shape[1],
                "height": h / image.shape[0],
                "confidence": 1.0  # Haar分类器不提供置信度
            }

            # 检测眼睛
            roi_gray = gray[y:y+h, x:x+w]
            eyes = eye_cascade.detectMultiScale(roi_gray)
            face["eyes"] = []
            for (ex, ey, ew, eh) in eyes:
                face["eyes"].append({
                    "x": (x + ex + ew / 2) / image.shape[1],
                    "y": (y + ey + eh / 2) / image.shape[0],
                    "width": ew / image.shape[1],
                    "height": eh / image.shape[0]
                })

            results.append(face)

        return {"faces": results, "count": len(results)}

    except Exception as e:
        import traceback
        error_detail = f"人脸检测失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

class BackgroundSubtractRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    method: str = Field("mog2", description="方法: mog2 或 knn")

@app.post("/api/opencv/background-subtract")
async def opencv_background_subtract(request: BackgroundSubtractRequest):
    """背景减除（需要视频帧序列）"""
    # 注意：背景减除通常需要视频流，这里返回演示信息
    return {
        "message": "背景减除功能需要视频流支持，将在视频处理模块中实现",
        "supported_methods": ["MOG2", "KNN"],
        "usage": "使用视频检测模块进行背景减除"
    }

class LaneDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    canny_threshold1: int = Field(50, description="Canny低阈值")
    canny_threshold2: int = Field(150, description="Canny高阈值")

@app.post("/api/realtime/lane-detect")
async def lane_detection(request: LaneDetectRequest):
    """车道线检测"""
    try:
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            raise HTTPException(status_code=404, detail="图片不存在")

        img = cv2.imread(str(image_path))
        if img is None:
            raise HTTPException(status_code=400, detail="图片读取失败")

        height, width = img.shape[:2]

        # 转换为灰度图
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

        # 高斯模糊
        blurred = cv2.GaussianBlur(gray, (5, 5), 0)

        # Canny边缘检测
        edges = cv2.Canny(blurred, request.canny_threshold1, request.canny_threshold2)

        # 感兴趣区域（下半部分）
        mask = np.zeros_like(edges)
        roi_vertices = np.array([
            [(0, height), (width / 2, height / 2), (width, height)]
        ], dtype=np.int32)
        cv2.fillPoly(mask, roi_vertices, 255)
        masked_edges = cv2.bitwise_and(edges, mask)

        # 霍夫变换检测直线
        lines = cv2.HoughLinesP(
            masked_edges,
            rho=1,
            theta=np.pi / 180,
            threshold=50,
            minLineLength=100,
            maxLineGap=50
        )

        # 处理检测到的线
        lane_lines = []
        if lines is not None:
            for line in lines:
                x1, y1, x2, y2 = line[0]
                # 归一化坐标
                lane_lines.append({
                    "x1": float(x1) / width,
                    "y1": float(y1) / height,
                    "x2": float(x2) / width,
                    "y2": float(y2) / height
                })

        return {
            "success": True,
            "lane_lines": lane_lines,
            "count": len(lane_lines)
        }

    except Exception as e:
        import traceback
        error_detail = f"车道线检测失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

class HandDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")

@app.post("/api/realtime/hand-detect")
async def hand_detection(request: HandDetectRequest):
    """手部检测（使用皮肤颜色检测）"""
    try:
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            raise HTTPException(status_code=404, detail="图片不存在")

        img = cv2.imread(str(image_path))
        if img is None:
            raise HTTPException(status_code=400, detail="图片读取失败")

        # 转换为HSV颜色空间
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)

        # 定义肤色范围（可以调整）
        lower_skin = np.array([0, 20, 70], dtype=np.uint8)
        upper_skin = np.array([20, 255, 255], dtype=np.uint8)

        # 创建肤色掩码
        skin_mask = cv2.inRange(hsv, lower_skin, upper_skin)

        # 形态学操作去除噪声
        kernel = np.ones((3, 3), np.uint8)
        skin_mask = cv2.morphologyEx(skin_mask, cv2.MORPH_OPEN, kernel, iterations=2)
        skin_mask = cv2.morphologyEx(skin_mask, cv2.MORPH_DILATE, kernel, iterations=1)

        # 查找轮廓
        contours, _ = cv2.findContours(skin_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        # 过滤小轮廓
        min_area = 1000
        hands = []
        height, width = img.shape[:2]

        for contour in contours:
            area = cv2.contourArea(contour)
            if area > min_area:
                # 获取边界框
                x, y, w, h = cv2.boundingRect(contour)
                
                # 计算凸包
                hull = cv2.convexHull(contour)
                hull_area = cv2.contourArea(hull)
                
                # 计算凸包缺陷（检测手指）
                finger_count = 0
                if len(hull) > 3:
                    defects = cv2.convexityDefects(contour, hull)
                    if defects is not None:
                        for i in range(defects.shape[0]):
                            s, e, f, d = defects[i, 0]
                            if d > 10000:  # 缺陷深度阈值
                                finger_count += 1

                hands.append({
                    "x": x / width,
                    "y": y / height,
                    "width": w / width,
                    "height": h / height,
                    "area": area,
                    "fingers": min(finger_count + 1, 5)  # 手指数量
                })

        return {
            "success": True,
            "hands": hands,
            "count": len(hands)
        }

    except Exception as e:
        import traceback
        error_detail = f"手部检测失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

# ==================== 摄像头支持 ====================

# 全局摄像头对象
camera_capture = None
camera_active = False

# 缓存模型（避免重复加载）
_face_cascade = None

def get_face_cascade():
    """获取人脸检测器（懒加载）"""
    global _face_cascade
    if _face_cascade is None:
        _face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
    return _face_cascade

class CameraInitRequest(BaseModel):
    camera_id: int = Field(0, description="摄像头ID，0为默认摄像头")
    width: int = Field(640, description="视频宽度")
    height: int = Field(480, description="视频高度")

class CameraFrameRequest(BaseModel):
    operation: str = Field(..., description="操作类型: capture, face_detect, lane_detect, hand_detect")

@app.post("/api/camera/init")
async def init_camera(request: CameraInitRequest):
    """初始化摄像头"""
    global camera_capture, camera_active
    
    try:
        # 如果已经有摄像头，先关闭
        if camera_capture is not None:
            camera_capture.release()
            camera_capture = None
        
        # 打开摄像头
        camera_capture = cv2.VideoCapture(request.camera_id)
        
        if not camera_capture.isOpened():
            raise HTTPException(status_code=500, detail="无法打开摄像头")
        
        # 设置分辨率（使用较低分辨率以提高性能）
        camera_capture.set(cv2.CAP_PROP_FRAME_WIDTH, request.width)
        camera_capture.set(cv2.CAP_PROP_FRAME_HEIGHT, request.height)
        camera_capture.set(cv2.CAP_PROP_FPS, 60)  # 设置60FPS
        camera_capture.set(cv2.CAP_PROP_BUFFERSIZE, 1)  # 减少缓冲区大小
        
        # 降低MJPEG质量以提高速度
        camera_capture.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc('M', 'J', 'P', 'G'))
        camera_capture.set(cv2.CAP_PROP_FPS, 60)
        
        camera_active = True
        
        return {
            "success": True,
            "message": f"摄像头初始化成功",
            "width": int(camera_capture.get(cv2.CAP_PROP_FRAME_WIDTH)),
            "height": int(camera_capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
        }
    
    except Exception as e:
        import traceback
        error_detail = f"摄像头初始化失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        camera_active = False
        raise HTTPException(status_code=500, detail=error_detail)

@app.post("/api/camera/frame")
async def get_camera_frame(request: CameraFrameRequest):
    """获取摄像头帧并执行操作"""
    global camera_capture, camera_active
    
    try:
        if camera_capture is None or not camera_active:
            raise HTTPException(status_code=400, detail="摄像头未初始化")
        
        # 读取一帧
        ret, frame = camera_capture.read()
        
        if not ret or frame is None:
            raise HTTPException(status_code=500, detail="无法读取摄像头帧")
        
        height, width = frame.shape[:2]
        
        # 根据操作类型执行不同的处理
        result = {
            "success": True,
            "width": width,
            "height": height,
            "operation": request.operation
        }
        
        if request.operation == "capture":
            # 纯捕获帧 - 使用较低JPEG质量提高速度
            _, buffer = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 75])
            result["image"] = base64.b64encode(buffer).decode('utf-8')
            
        elif request.operation == "face_detect":
            # 人脸检测 - 使用缓存的分类器
            face_cascade = get_face_cascade()
            
            # 缩小图像以加速检测
            frame_small = cv2.resize(frame, (width // 2, height // 2))
            gray = cv2.cvtColor(frame_small, cv2.COLOR_BGR2GRAY)
            faces = face_cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(30, 30))
            
            faces_list = []
            scale = 2.0  # 坐标缩放回去
            
            for (x, y, w, h) in faces:
                x, y, w, h = int(x * scale), int(y * scale), int(w * scale), int(h * scale)
                faces_list.append({
                    "x": float(x) / width,
                    "y": float(y) / height,
                    "width": float(w) / width,
                    "height": float(h) / height
                })
            
            result["faces"] = faces_list
            result["count"] = len(faces_list)
            
            # 绘制人脸框
            for (x, y, w, h) in faces:
                cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 0), 2)
            
            _, buffer = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
            result["image"] = base64.b64encode(buffer).decode('utf-8')
            
        elif request.operation == "lane_detect":
            # 车道线检测 - 缩小图像加速
            frame_small = cv2.resize(frame, (width // 2, height // 2))
            small_h, small_w = frame_small.shape[:2]
            
            gray = cv2.cvtColor(frame_small, cv2.COLOR_BGR2GRAY)
            blurred = cv2.GaussianBlur(gray, (3, 3), 0)
            edges = cv2.Canny(blurred, 50, 150)
            
            # 感兴趣区域
            mask = np.zeros_like(edges)
            roi_vertices = np.array([[(0, small_h), (small_w/2, small_h/2), (small_w, small_h)]], dtype=np.int32)
            cv2.fillPoly(mask, roi_vertices, 255)
            masked_edges = cv2.bitwise_and(edges, mask)
            
            lines = cv2.HoughLinesP(masked_edges, 1, np.pi/180, 30, minLineLength=50, maxLineGap=30)
            
            lane_lines = []
            if lines is not None:
                for line in lines:
                    x1, y1, x2, y2 = line[0]
                    # 坐标缩放回去
                    x1, y1, x2, y2 = x1*2, y1*2, x2*2, y2*2
                    cv2.line(frame, (x1, y1), (x2, y2), (0, 255, 255), 2)
                    lane_lines.append({
                        "x1": float(x1) / width,
                        "y1": float(y1) / height,
                        "x2": float(x2) / width,
                        "y2": float(y2) / height
                    })
            
            result["lane_lines"] = lane_lines
            result["count"] = len(lane_lines)
            
            _, buffer = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
            result["image"] = base64.b64encode(buffer).decode('utf-8')
            
        elif request.operation == "hand_detect":
            # 手势识别 - 缩小图像加速处理
            frame_small = cv2.resize(frame, (width // 2, height // 2))
            small_h, small_w = frame_small.shape[:2]
            
            hsv = cv2.cvtColor(frame_small, cv2.COLOR_BGR2HSV)
            lower_skin = np.array([0, 20, 70], dtype=np.uint8)
            upper_skin = np.array([20, 255, 255], dtype=np.uint8)
            
            skin_mask = cv2.inRange(hsv, lower_skin, upper_skin)
            kernel = np.ones((3, 3), np.uint8)
            skin_mask = cv2.morphologyEx(skin_mask, cv2.MORPH_OPEN, kernel, iterations=1)
            skin_mask = cv2.morphologyEx(skin_mask, cv2.MORPH_DILATE, kernel, iterations=1)
            
            contours, _ = cv2.findContours(skin_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            hands = []
            min_area = 250  # 降低最小面积阈值以提高速度
            
            for contour in contours:
                area = cv2.contourArea(contour)
                if area > min_area:
                    x, y, w, h = cv2.boundingRect(contour)
                    hull = cv2.convexHull(contour)
                    
                    finger_count = 0
                    if len(hull) > 3:
                        try:
                            defects = cv2.convexityDefects(contour, hull)
                            if defects is not None and len(defects) > 0:
                                for i in range(defects.shape[0]):
                                    s, e, f, d = defects[i, 0]
                                    if d > 5000:  # 降低缺陷深度阈值
                                        finger_count += 1
                        except cv2.error:
                            # 忽略无法计算凸包缺陷的轮廓
                            pass
                    
                    fingers = min(finger_count + 1, 5)
                    # 坐标缩放回原始尺寸
                    x, y, w, h = x*2, y*2, w*2, h*2
                    hands.append({
                        "x": x / width,
                        "y": y / height,
                        "width": w / width,
                        "height": h / height,
                        "fingers": fingers
                    })
                    
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 255), 2)
                    cv2.putText(frame, f"{fingers}", (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 1)
            
            result["hands"] = hands
            result["count"] = len(hands)
            
            # 使用较低JPEG质量
            _, buffer = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
            result["image"] = base64.b64encode(buffer).decode('utf-8')
        
        return result
    
    except Exception as e:
        import traceback
        error_detail = f"摄像头帧处理失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

@app.post("/api/camera/stop")
async def stop_camera():
    """关闭摄像头"""
    global camera_capture, camera_active
    
    try:
        if camera_capture is not None:
            camera_capture.release()
            camera_capture = None
        
        camera_active = False
        
        return {
            "success": True,
            "message": "摄像头已关闭"
        }
    
    except Exception as e:
        import traceback
        error_detail = f"关闭摄像头失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

@app.delete("/api/training-data")
async def delete_training_data():
    """删除所有训练数据（模型和数据集）"""
    try:
        import shutil

        deleted_models = []
        deleted_datasets = []

        # 删除模型目录
        if MODELS_DIR.exists():
            for model_dir in MODELS_DIR.iterdir():
                if model_dir.is_dir():
                    shutil.rmtree(model_dir)
                    deleted_models.append(model_dir.name)
            print(f"[DEBUG] 已删除模型: {deleted_models}")

        # 删除数据集目录
        if DATASETS_DIR.exists():
            shutil.rmtree(DATASETS_DIR)
            DATASETS_DIR.mkdir(parents=True, exist_ok=True)
            deleted_datasets.append("datasets")

        # 重置全局模型
        global current_model
        current_model = None

        return {
            "message": "训练数据删除成功",
            "deleted_models": deleted_models,
            "deleted_datasets": deleted_datasets
        }
    except Exception as e:
        import traceback
        error_detail = f"删除训练数据失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        raise HTTPException(status_code=500, detail=error_detail)

@app.post("/api/reload-model")
async def reload_model():
    """重新加载模型"""
    global current_model
    current_model = None
    return {"message": "模型已重置，下次检测时将重新加载"}

@app.get("/api/models")
async def list_models():
    """列出所有训练好的模型"""
    models = []
    for model_dir in MODELS_DIR.iterdir():
        if model_dir.is_dir():
            best_pt = model_dir / "best.pt"
            best_onnx = model_dir / "best.onnx"
            model_info = {"name": model_dir.name, "path": str(model_dir)}
            if best_pt.exists():
                model_info["pt"] = str(best_pt)
            if best_onnx.exists():
                model_info["onnx"] = str(best_onnx)
            models.append(model_info)

    return {"models": models}

# ==================== 视频检测 ====================

class VideoDetectionRequest(BaseModel):
    source: str  # 视频文件路径、摄像头索引(如0)或RTSP流地址
    source_type: str = "file"  # file, camera, rtsp
    model_type: str = "yolov8n"  # 模型类型
    confidence_threshold: float = 0.5  # 置信度阈值

class VideoDetectionResponse(BaseModel):
    success: bool
    message: str
    frame_count: int = 0
    video_info: Optional[Dict[str, Any]] = None

# 全局视频检测状态
video_detection_active = False
video_capture = None

@app.post("/api/video/detect/start")
async def start_video_detection(request: VideoDetectionRequest):
    """开始视频检测（支持本地视频文件、摄像头和RTSP流）"""
    global video_detection_active, video_capture, current_model

    if video_detection_active:
        return VideoDetectionResponse(
            success=False,
            message="视频检测已在运行中"
        )

    try:
        print(f"[VIDEO] 收到视频检测请求")
        print(f"[VIDEO] 源类型: {request.source_type}, 源地址: {request.source}")
        print(f"[VIDEO] 模型类型: {request.model_type}, 置信度阈值: {request.confidence_threshold}")

        # 查找模型文件
        model_files = list(MODELS_DIR.glob("**/best.onnx"))
        if not model_files:
            pt_files = list(MODELS_DIR.glob("**/best.pt"))
            if not pt_files:
                # 使用默认预训练模型
                model_path = f"{request.model_type}.pt"
                print(f"[VIDEO] 使用默认预训练模型: {model_path}")
            else:
                model_path = pt_files[-1]
                print(f"[VIDEO] 使用训练好的模型: {model_path}")
        else:
            model_path = model_files[-1]
            print(f"[VIDEO] 使用训练好的模型: {model_path}")

        print(f"[VIDEO] 模型路径: {model_path}")

        # 加载模型
        if current_model is None:
            print(f"[VIDEO] 正在加载模型...")
            current_model = YOLO(str(model_path))
            print(f"[VIDEO] 模型加载成功")

        # 打开视频源
        source = request.source
        source_type = request.source_type
        
        print(f"[VIDEO] 正在打开视频源: type={source_type}, source={source}")
        
        if source_type == "camera":
            # 本地摄像头
            try:
                camera_index = int(source)
                video_capture = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
                print(f"[VIDEO] 尝试打开本地摄像头索引: {camera_index}")
            except:
                video_capture = cv2.VideoCapture(0, cv2.CAP_DSHOW)
                print(f"[VIDEO] 使用默认摄像头索引 0")
        elif source_type == "rtsp":
            # RTSP流 - 添加RTSP优化参数
            rtsp_url = source
            print(f"[VIDEO] 打开RTSP流: {rtsp_url}")
            video_capture = cv2.VideoCapture(rtsp_url, cv2.CAP_FFMPEG)
            # RTSP传输优化参数
            video_capture.set(cv2.CAP_PROP_BUFFERSIZE, 3)
        else:
            # 文件
            file_path = Path(source)
            if not file_path.exists():
                # 尝试相对路径
                file_path = IMAGES_DIR / source
            print(f"[VIDEO] 打开视频文件: {file_path}")
            video_capture = cv2.VideoCapture(str(file_path))

        if not video_capture.isOpened():
            error_msg = f"无法打开视频源: {source}"
            print(f"[VIDEO] {error_msg}")
            return VideoDetectionResponse(
                success=False,
                message=error_msg
            )

        print(f"[VIDEO] 视频源打开成功")

        # 获取视频信息
        fps = video_capture.get(cv2.CAP_PROP_FPS)
        frame_count = int(video_capture.get(cv2.CAP_PROP_FRAME_COUNT))
        width = int(video_capture.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(video_capture.get(cv2.CAP_PROP_FRAME_HEIGHT))

        video_info = {
            "fps": fps if fps > 0 else 30.0,
            "frame_count": frame_count,
            "width": width,
            "height": height,
            "source_type": source_type
        }

        print(f"[VIDEO] 视频信息: fps={video_info['fps']}, frames={frame_count}, resolution={width}x{height}")

        video_detection_active = True

        return VideoDetectionResponse(
            success=True,
            message="视频检测已启动",
            frame_count=frame_count,
            video_info=video_info
        )

    except Exception as e:
        import traceback
        error_detail = f"启动视频检测失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        return VideoDetectionResponse(
            success=False,
            message=error_detail
        )

@app.post("/api/video/detect/stop")
async def stop_video_detection():
    """停止视频检测"""
    global video_detection_active, video_capture

    try:
        video_detection_active = False

        if video_capture is not None:
            video_capture.release()
            video_capture = None
            print(f"[DEBUG] 视频检测已停止")

        return {"success": True, "message": "视频检测已停止"}

    except Exception as e:
        import traceback
        error_detail = f"停止视频检测失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        return {"success": False, "message": error_detail}

@app.get("/api/video/detect/frame")
async def get_video_frame(confidence_threshold: float = 0.5, end2end: bool = True):
    """获取视频帧的检测结果"""
    global video_detection_active, video_capture, current_model

    if not video_detection_active or video_capture is None:
        return {"success": False, "message": "视频检测未运行"}

    try:
        # 读取一帧
        ret, frame = video_capture.read()

        if not ret:
            print(f"[VIDEO] 视频结束或读取失败")
            return {"success": False, "message": "视频结束或读取失败"}

        # 获取当前帧位置
        frame_pos = int(video_capture.get(cv2.CAP_PROP_POS_FRAMES))
        print(f"[VIDEO] 读取帧: {frame_pos}, end2end={end2end}")

        # 获取模型类别映射
        model_classes = []
        try:
            data_yaml = DATASETS_DIR / "data.yaml"
            if data_yaml.exists():
                import yaml
                with data_yaml.open("r", encoding='utf-8') as f:
                    data = yaml.safe_load(f)
                names = data.get("names", {})
                if isinstance(names, dict):
                    model_classes = [names[i] for i in sorted(names.keys())]
                elif isinstance(names, list):
                    model_classes = names
            print(f"[VIDEO] 模型类别: {model_classes}")
        except Exception as e:
            print(f"[VIDEO] 获取模型类别失败: {e}")

        # 进行检测
        detections = []
        if current_model is not None:
            try:
                results = current_model(frame, conf=confidence_threshold, end2end=end2end)
                for result in results:
                    # 检查是否是分割模型
                    if hasattr(result, 'masks') and result.masks is not None:
                        # 分割模型
                        masks = result.masks
                        boxes = result.boxes
                        for i, box in enumerate(boxes):
                            x1, y1, x2, y2 = box.xyxy[0].tolist()
                            x_center = (x1 + x2) / 2 / result.orig_shape[1]
                            y_center = (y1 + y2) / 2 / result.orig_shape[0]
                            width = (x2 - x1) / result.orig_shape[1]
                            height = (y2 - y1) / result.orig_shape[0]
                            class_id = int(box.cls[0])

                            # 获取多边形点（归一化坐标）
                            points = []
                            if i < len(masks.xy):
                                mask_points = masks.xy[i].tolist()  # 原始像素坐标
                                for px, py in mask_points:
                                    points.append([px / result.orig_shape[1], py / result.orig_shape[0]])

                            # 根据 class_id 获取类别名称
                            class_name = ""
                            if 0 <= class_id < len(model_classes):
                                class_name = model_classes[class_id]
                            else:
                                class_name = f"class_{class_id}"

                            detections.append({
                                "x": x_center,
                                "y": y_center,
                                "width": width,
                                "height": height,
                                "class_id": class_id,
                                "class_name": class_name,
                                "confidence": float(box.conf[0]),
                                "annotation_type": "polygon",
                                "points": points
                            })
                    elif hasattr(result, 'obb') and result.obb is not None:
                        # OBB 模型
                        obb_boxes = result.obb
                        for box in obb_boxes:
                            # OBB 格式: x_center, y_center, width, height, angle (弧度)
                            x_center, y_center, width, height, angle = box.xywhr[0].tolist()
                            class_id = int(box.cls[0])

                            # 归一化坐标
                            x_center = x_center / result.orig_shape[1]
                            y_center = y_center / result.orig_shape[0]
                            width = width / result.orig_shape[1]
                            height = height / result.orig_shape[0]

                            # 根据 class_id 获取类别名称
                            class_name = ""
                            if 0 <= class_id < len(model_classes):
                                class_name = model_classes[class_id]
                            else:
                                class_name = f"class_{class_id}"

                            detections.append({
                                "x": x_center,
                                "y": y_center,
                                "width": width,
                                "height": height,
                                "class_id": class_id,
                                "class_name": class_name,
                                "confidence": float(box.conf[0]),
                                "annotation_type": "obb",
                                "angle": angle
                            })
                    else:
                        # 检测模型
                        boxes = result.boxes
                        for box in boxes:
                            x1, y1, x2, y2 = box.xyxy[0].tolist()
                            x_center = (x1 + x2) / 2 / result.orig_shape[1]
                            y_center = (y1 + y2) / 2 / result.orig_shape[0]
                            width = (x2 - x1) / result.orig_shape[1]
                            height = (y2 - y1) / result.orig_shape[0]
                            class_id = int(box.cls[0])

                            # 根据 class_id 获取类别名称
                            class_name = ""
                            if 0 <= class_id < len(model_classes):
                                class_name = model_classes[class_id]
                            else:
                                class_name = f"class_{class_id}"

                            detections.append({
                                "x": x_center,
                                "y": y_center,
                                "width": width,
                                "height": height,
                                "class_id": class_id,
                                "class_name": class_name,
                                "confidence": float(box.conf[0]),
                                "annotation_type": "bbox"
                            })
                print(f"[VIDEO] 帧 {frame_pos}: 检测到 {len(detections)} 个目标")
            except Exception as e:
                print(f"[VIDEO] 检测错误: {e}")
        else:
            print(f"[VIDEO] 模型未加载，跳过检测")

        # 将帧编码为base64
        _, buffer = cv2.imencode('.jpg', frame)
        frame_base64 = base64.b64encode(buffer).decode()

        return {
            "success": True,
            "frame": frame_base64,
            "detections": detections,
            "frame_pos": frame_pos
        }

    except Exception as e:
        import traceback
        error_detail = f"获取视频帧失败: {str(e)}\n{traceback.format_exc()}"
        print(f"[ERROR] {error_detail}")
        return {"success": False, "message": error_detail}

@app.post("/api/video/detect/seek")
async def seek_video_frame(frame_pos: int = 0):
    """跳转到指定帧"""
    global video_detection_active, video_capture

    if not video_detection_active or video_capture is None:
        return {"success": False, "message": "视频检测未运行"}

    try:
        video_capture.set(cv2.CAP_PROP_POS_FRAMES, frame_pos)
        return {"success": True, "message": f"已跳转到帧 {frame_pos}"}

    except Exception as e:
        return {"success": False, "message": f"跳转失败: {str(e)}"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)


# ==================== 预训练模型自动标注 API ====================

class PretrainedModelRequest(BaseModel):
    """预训练模型推理请求"""
    image_name: str
    task_type: str = "detection"  # detection, segmentation, pose, obb
    model_size: str = "n"  # n, s, m, l, x
    conf_threshold: float = 0.25
    iou_threshold: float = 0.7

@app.post("/api/pretrained/annotate")
async def pretrained_annotate(request: PretrainedModelRequest):
    """
    使用预训练模型自动标注
    
    支持的任务类型：
    - detection: 目标检测 (yolo26n.pt)
    - segmentation: 实例分割 (yolo26n-seg.pt)
    - pose: 姿态估计 (yolo26n-pose.pt)
    - obb: 旋转框检测 (yolo26n-obb.pt)
    """
    try:
        print(f"[PRETRAINED] 开始自动标注: {request.image_name}, 任务={request.task_type}, 模型={request.model_size}")
        
        # 构建模型名称
        model_base = f"yolo26{request.model_size}"
        if request.task_type == "segmentation":
            model_name = f"{model_base}-seg.pt"
        elif request.task_type == "pose":
            model_name = f"{model_base}-pose.pt"
        elif request.task_type == "obb":
            model_name = f"{model_base}-obb.pt"
        else:
            model_name = f"{model_base}.pt"
        
        print(f"[PRETRAINED] 加载模型: {model_name}")
        
        # 加载预训练模型
        model = YOLO(model_name)
        
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            raise HTTPException(status_code=404, detail="图片不存在")
        
        # 进行推理
        results = model(str(image_path), conf=request.conf_threshold, iou=request.iou_threshold, verbose=False)
        
        # 解析结果
        detections = []
        for result in results:
            img_height, img_width = result.orig_shape
            
            # 姿态估计
            if hasattr(result, 'keypoints') and result.keypoints is not None:
                keypoints_data = result.keypoints
                boxes = result.boxes
                
                for i, box in enumerate(boxes):
                    x1, y1, x2, y2 = box.xyxy[0].tolist()
                    x_center = (x1 + x2) / 2 / img_width
                    y_center = (y1 + y2) / 2 / img_height
                    width = (x2 - x1) / img_width
                    height = (y2 - y1) / img_height
                    class_id = int(box.cls[0])
                    
                    # 获取关键点（归一化坐标）
                    keypoints = []
                    if i < len(keypoints_data.xy):
                        kpts = keypoints_data.xy[i].tolist()  # [[x, y], ...]
                        for kpt in kpts:
                            keypoints.append([kpt[0] / img_width, kpt[1] / img_height])
                    
                    detections.append({
                        "x": x_center,
                        "y": y_center,
                        "width": width,
                        "height": height,
                        "class_id": class_id,
                        "class_name": model.names[class_id],
                        "confidence": float(box.conf[0]),
                        "annotation_type": "keypoints",
                        "keypoints": keypoints
                    })
            
            # 实例分割
            elif hasattr(result, 'masks') and result.masks is not None:
                masks = result.masks
                boxes = result.boxes
                
                for i, box in enumerate(boxes):
                    x1, y1, x2, y2 = box.xyxy[0].tolist()
                    x_center = (x1 + x2) / 2 / img_width
                    y_center = (y1 + y2) / 2 / img_height
                    width = (x2 - x1) / img_width
                    height = (y2 - y1) / img_height
                    class_id = int(box.cls[0])
                    
                    # 获取多边形点（归一化坐标）
                    points = []
                    if i < len(masks.xy):
                        mask_points = masks.xy[i].tolist()
                        for px, py in mask_points:
                            points.append([px / img_width, py / img_height])
                    
                    detections.append({
                        "x": x_center,
                        "y": y_center,
                        "width": width,
                        "height": height,
                        "class_id": class_id,
                        "class_name": model.names[class_id],
                        "confidence": float(box.conf[0]),
                        "annotation_type": "polygon",
                        "points": points
                    })
            
            # 旋转框检测
            elif hasattr(result, 'obb') and result.obb is not None:
                obb_boxes = result.obb
                
                for box in obb_boxes:
                    x_center, y_center, width, height, angle = box.xywhr[0].tolist()
                    class_id = int(box.cls[0])
                    
                    # 归一化坐标
                    x_center = x_center / img_width
                    y_center = y_center / img_height
                    width = width / img_width
                    height = height / img_height
                    
                    detections.append({
                        "x": x_center,
                        "y": y_center,
                        "width": width,
                        "height": height,
                        "class_id": class_id,
                        "class_name": model.names[class_id],
                        "confidence": float(box.conf[0]),
                        "annotation_type": "obb",
                        "angle": angle
                    })
            
            # 目标检测（默认）
            else:
                boxes = result.boxes
                
                for box in boxes:
                    x1, y1, x2, y2 = box.xyxy[0].tolist()
                    x_center = (x1 + x2) / 2 / img_width
                    y_center = (y1 + y2) / 2 / img_height
                    width = (x2 - x1) / img_width
                    height = (y2 - y1) / img_height
                    class_id = int(box.cls[0])
                    
                    detections.append({
                        "x": x_center,
                        "y": y_center,
                        "width": width,
                        "height": height,
                        "class_id": class_id,
                        "class_name": model.names[class_id],
                        "confidence": float(box.conf[0]),
                        "annotation_type": "bbox"
                    })
        
        print(f"[PRETRAINED] 检测到 {len(detections)} 个目标")
        
        return {
            "success": True,
            "image_name": request.image_name,
            "task_type": request.task_type,
            "model": model_name,
            "detections": detections,
            "count": len(detections)
        }
        
    except Exception as e:
        import traceback
        print(f"[ERROR] 预训练模型标注失败: {e}")
        print(f"[ERROR] 详细错误:\n{traceback.format_exc()}")
        return {
            "success": False,
            "error": str(e)
        }


class BatchPretrainedRequest(BaseModel):
    """批量预训练模型推理请求"""
    image_names: List[str]
    task_type: str = "detection"
    model_size: str = "n"
    conf_threshold: float = 0.25
    iou_threshold: float = 0.7
    save_annotations: bool = True

@app.post("/api/pretrained/batch-annotate")
async def batch_pretrained_annotate(request: BatchPretrainedRequest):
    """
    批量使用预训练模型自动标注
    """
    try:
        print(f"[BATCH_PRETRAINED] 开始批量标注: {len(request.image_names)} 张图片, 任务={request.task_type}")
        
        results_list = []
        success_count = 0
        failed_count = 0
        
        # 对每张图片调用单张标注逻辑
        for idx, image_name in enumerate(request.image_names):
            try:
                print(f"[BATCH_PRETRAINED] 处理 {idx+1}/{len(request.image_names)}: {image_name}")
                
                # 创建单张图片的请求
                single_request = PretrainedModelRequest(
                    image_name=image_name,
                    task_type=request.task_type,
                    model_size=request.model_size,
                    conf_threshold=request.conf_threshold,
                    iou_threshold=request.iou_threshold
                )
                
                # 调用单张标注接口
                result = await pretrained_annotate(single_request)
                
                if result and "detections" in result:
                    detections = result["detections"]
                    detection_count = len(detections)
                    
                    # 如果需要保存标注
                    if request.save_annotations and detections:
                        image_path = IMAGES_DIR / image_name
                        img = cv2.imread(str(image_path))
                        height, width = img.shape[:2]
                        
                        annotation_data = {
                            "image_name": image_name,
                            "width": width,
                            "height": height,
                            "bboxes": detections
                        }
                        
                        annotation_path = ANNOTATIONS_DIR / f"{image_name}.json"
                        with annotation_path.open("w", encoding="utf-8") as f:
                            json.dump(annotation_data, f, indent=2, ensure_ascii=False)
                    
                    results_list.append({
                        "image_name": image_name,
                        "success": True,
                        "detection_count": detection_count,
                        "error": None
                    })
                    success_count += 1
                    print(f"[BATCH_PRETRAINED] ✓ {image_name}: {detection_count} 个目标")
                else:
                    results_list.append({
                        "image_name": image_name,
                        "success": True,
                        "detection_count": 0,
                        "error": None
                    })
                    success_count += 1
                    print(f"[BATCH_PRETRAINED] ✓ {image_name}: 0 个目标")
                
            except Exception as e:
                results_list.append({
                    "image_name": image_name,
                    "success": False,
                    "detection_count": 0,
                    "error": str(e)
                })
                failed_count += 1
                print(f"[BATCH_PRETRAINED] ✗ {image_name}: {str(e)}")
        
        print(f"[BATCH_PRETRAINED] 完成: 成功 {success_count}, 失败 {failed_count}, 总计 {len(request.image_names)}")
        
        return {
            "total_count": len(request.image_names),
            "success_count": success_count,
            "failed_count": failed_count,
            "results": results_list
        }
        
    except Exception as e:
        import traceback
        print(f"[ERROR] 批量预训练模型标注失败: {e}")
        print(f"[ERROR] 详细错误:\n{traceback.format_exc()}")
        raise HTTPException(status_code=500, detail=str(e))
