"""
API 扩展 - 模型管理和批量推理相关的 API 端点
"""
from fastapi import BackgroundTasks
from pydantic import BaseModel, Field
from typing import List, Optional, Dict, Any
import json
import cv2
import asyncio

# from models.model_manager import model_manager
# 注意：model_manager 模块暂未实现，相关功能将被禁用
from main import BASE_DIR, IMAGES_DIR, ANNOTATIONS_DIR


# ==================== 模型管理 API ====================

async def get_available_models(task_type: Optional[str] = None):
    """获取可用模型列表"""
    try:
        models = model_manager.get_available_models(task_type)
        return {"success": True, "models": models}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def get_model_info(model_name: str):
    """获取模型详细信息"""
    try:
        info = model_manager.get_model_info(model_name)
        if info:
            return {"success": True, "info": info}
        else:
            return {"success": False, "error": "模型不存在"}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def load_model_api(model_name: str, device: str = "cpu"):
    """加载模型"""
    try:
        model = await model_manager.load_model(model_name, device)
        if model:
            return {"success": True, "message": f"模型 {model_name} 加载成功"}
        else:
            return {"success": False, "error": f"模型 {model_name} 加载失败"}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def unload_model_api(model_name: str):
    """卸载模型"""
    try:
        model_manager.unload_model(model_name)
        return {"success": True, "message": f"模型 {model_name} 已卸载"}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def preload_models_api(device: str = "cpu"):
    """预加载标记为预加载的模型"""
    try:
        await model_manager.preload_models(device)
        return {"success": True, "message": "模型预加载完成"}
    except Exception as e:
        return {"success": False, "error": str(e)}

async def unload_all_models_api():
    """卸载所有模型"""
    try:
        model_manager.unload_all_models()
        return {"success": True, "message": "所有模型已卸载"}
    except Exception as e:
        return {"success": False, "error": str(e)}


# ==================== 批量推理 API ====================

class BatchDetectRequest(BaseModel):
    image_names: List[str] = Field(..., description="图片名称列表")
    model_name: str = Field("yolov8n", description="模型名称")
    conf_threshold: float = Field(0.25, description="置信度阈值")
    iou_threshold: float = Field(0.45, description="NMS IOU 阈值")
    device: str = Field("cpu", description="运行设备")

async def batch_detect(request: BatchDetectRequest, background_tasks: BackgroundTasks):
    """批量检测"""
    try:
        # 在后台任务中执行批量检测
        background_tasks.add_task(
            _run_batch_detection,
            request.image_names,
            request.model_name,
            request.conf_threshold,
            request.iou_threshold,
            request.device
        )
        
        return {
            "success": True,
            "message": f"批量检测任务已启动，共 {len(request.image_names)} 张图片"
        }
    except Exception as e:
        return {"success": False, "error": str(e)}

def _run_batch_detection(image_names: List[str], model_name: str, conf_threshold: float, 
                         iou_threshold: float, device: str):
    """执行批量检测（后台任务）"""
    try:
        print(f"[BATCH] 开始批量检测，模型: {model_name}, 图片数量: {len(image_names)}")
        
        # 加载模型
        model = asyncio.run(model_manager.load_model(model_name, device))
        if not model:
            print(f"[BATCH] 模型加载失败: {model_name}")
            return
        
        # 逐张检测
        for i, image_name in enumerate(image_names):
            try:
                print(f"[BATCH] 处理图片 {i+1}/{len(image_names)}: {image_name}")
                
                # 读取图片
                image_path = IMAGES_DIR / image_name
                if not image_path.exists():
                    print(f"[BATCH] 图片不存在: {image_path}")
                    continue
                
                image = cv2.imread(str(image_path))
                if image is None:
                    print(f"[BATCH] 图片读取失败: {image_name}")
                    continue
                
                # 执行检测
                result = model.predict(image, conf_threshold=conf_threshold, iou_threshold=iou_threshold)
                detections = result.get("detections", [])
                
                print(f"[BATCH] 检测到 {len(detections)} 个目标")
                
                # 保存检测结果到标注文件
                annotation_path = ANNOTATIONS_DIR / f"{image_name}.json"
                
                # 读取现有标注（如果存在）
                if annotation_path.exists():
                    with annotation_path.open("r", encoding="utf-8") as f:
                        annotation_data = json.load(f)
                else:
                    annotation_data = {
                        "version": "5.5.0",
                        "shapes": [],
                        "image_name": image_name,
                        "width": image.shape[1],
                        "height": image.shape[0]
                    }
                
                # 添加检测结果
                for det in detections:
                    annotation_data["shapes"].append({
                        "label": det.get("class_name", "unknown"),
                        "score": det.get("confidence", 0.0),
                        "points": det.get("points", []),
                        "group_id": None,
                        "description": None,
                        "difficult": False,
                        "shape_type": det.get("annotation_type", "bbox"),
                        "flags": None,
                        "attributes": {}
                    })
                
                # 保存标注
                with annotation_path.open("w", encoding="utf-8") as f:
                    json.dump(annotation_data, f, indent=2, ensure_ascii=False)
                
                print(f"[BATCH] 标注已保存: {annotation_path}")
                
            except Exception as e:
                print(f"[BATCH] 处理图片失败 {image_name}: {e}")
                import traceback
                traceback.print_exc()
        
        print(f"[BATCH] 批量检测完成")
        
    except Exception as e:
        print(f"[BATCH] 批量检测失败: {e}")
        import traceback
        traceback.print_exc()


# ==================== AI 标注 API ====================

class AIAnnotationRequest(BaseModel):
    model_name: str = Field(..., description="模型名称")
    image_path: str = Field(..., description="图片路径")
    conf_threshold: float = Field(0.25, description="置信度阈值")
    iou_threshold: float = Field(0.45, description="NMS IOU 阈值")

async def ai_annotate(request: AIAnnotationRequest):
    """AI标注 - 单张图片"""
    try:
        print(f"[AI_ANNOTATE] 开始AI标注，模型: {request.model_name}, 图片: {request.image_path}")
        
        # 加载模型
        model = await model_manager.load_model(request.model_name)
        if not model:
            return {"success": False, "error": f"模型加载失败: {request.model_name}"}
        
        # 读取图片 - 从 IMAGES_DIR 拼接完整路径
        image_path = IMAGES_DIR / request.image_path
        if not image_path.exists():
            return {"success": False, "error": f"图片不存在: {image_path}"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 执行检测
        result = model.predict(
            image,
            conf_threshold=request.conf_threshold,
            iou_threshold=request.iou_threshold
        )
        
        # 提取检测结果
        detections = result.get("detections", [])
        
        print(f"[AI_ANNOTATE] 检测到 {len(detections)} 个目标")
        
        return {
            "success": True,
            "annotations": detections,
            "count": len(detections)
        }
    except Exception as e:
        print(f"[AI_ANNOTATE] AI标注失败: {e}")
        import traceback
        traceback.print_exc()
        return {"success": False, "error": str(e)}


# ==================== OCR 识别 API ====================

class OCRRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    model_name: str = Field("ppocr-v4", description="OCR 模型名称")
    device: str = Field("cpu", description="运行设备")

async def ocr_detect(request: OCRRequest):
    """OCR 文字识别"""
    try:
        # 加载 OCR 模型
        model = await model_manager.load_model(request.model_name, request.device)
        if not model:
            return {"success": False, "error": f"OCR 模型加载失败: {request.model_name}"}
        
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            return {"success": False, "error": "图片不存在"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 执行 OCR 识别
        result = model.predict(image)
        
        return {
            "success": True,
            "detections": result.get("detections", []),
            "texts": result.get("texts", [])
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


# ==================== SAM 分割 API ====================

class SAMPrompt(BaseModel):
    """SAM 提示词"""
    type: str = Field(..., description="提示类型: point, box")
    x: Optional[float] = Field(None, description="点的 x 坐标（归一化 0-1）")
    y: Optional[float] = Field(None, description="点的 y 坐标（归一化 0-1）")
    label: Optional[int] = Field(1, description="点标签: 1=前景, 0=背景")
    x1: Optional[float] = Field(None, description="框左上角 x（归一化）")
    y1: Optional[float] = Field(None, description="框左上角 y（归一化）")
    x2: Optional[float] = Field(None, description="框右下角 x（归一化）")
    y2: Optional[float] = Field(None, description="框右下角 y（归一化）")

class SAMRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    model_name: str = Field("sam-b", description="SAM 模型名称")
    prompts: Optional[List[SAMPrompt]] = Field(None, description="提示词列表")
    auto_segment: bool = Field(False, description="是否自动分割")
    device: str = Field("cpu", description="运行设备")
    multimask_output: bool = Field(True, description="是否输出多个掩码")

async def sam_segment(request: SAMRequest):
    """SAM 交互式图像分割
    
    支持的提示类型：
    1. 点提示 (point): 点击物体的前景或背景
       - x, y: 归一化坐标 (0-1)
       - label: 1=前景点, 0=背景点
    
    2. 框提示 (box): 框选物体区域
       - x1, y1: 左上角坐标（归一化）
       - x2, y2: 右下角坐标（归一化）
    
    3. 自动分割: 不提供提示，自动分割整张图片
    
    示例：
    {
        "image_name": "test.jpg",
        "model_name": "sam-b",
        "prompts": [
            {"type": "point", "x": 0.5, "y": 0.5, "label": 1},
            {"type": "point", "x": 0.3, "y": 0.3, "label": 0}
        ]
    }
    """
    try:
        print(f"[SAM] 开始分割，图片: {request.image_name}, 模型: {request.model_name}")
        
        # 加载 SAM 模型
        model = await model_manager.load_model(request.model_name, request.device)
        if not model:
            return {"success": False, "error": f"SAM 模型加载失败: {request.model_name}"}
        
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            return {"success": False, "error": "图片不存在"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 转换提示词格式
        prompts_dict = None
        if request.prompts:
            prompts_dict = []
            for prompt in request.prompts:
                prompt_data = {"type": prompt.type}
                if prompt.type == "point":
                    prompt_data["x"] = prompt.x
                    prompt_data["y"] = prompt.y
                    prompt_data["label"] = prompt.label
                elif prompt.type == "box":
                    prompt_data["x1"] = prompt.x1
                    prompt_data["y1"] = prompt.y1
                    prompt_data["x2"] = prompt.x2
                    prompt_data["y2"] = prompt.y2
                prompts_dict.append(prompt_data)
            
            print(f"[SAM] 提示词: {prompts_dict}")
        
        # 执行分割
        result = model.predict(
            image, 
            prompts=prompts_dict, 
            auto_segment=request.auto_segment,
            multimask_output=request.multimask_output
        )
        
        detections = result.get("detections", [])
        print(f"[SAM] 分割完成，生成 {len(detections)} 个掩码")
        
        return {
            "success": True,
            "detections": detections,
            "count": len(detections),
            "task_type": "segmentation"
        }
    except Exception as e:
        print(f"[SAM] 分割失败: {e}")
        import traceback
        traceback.print_exc()
        return {"success": False, "error": str(e)}


# ==================== RT-DETR 检测 API ====================

class RTDETRDetectRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    model_name: str = Field("rtdetr-l", description="RT-DETR 模型名称")
    conf_threshold: float = Field(0.25, description="置信度阈值")
    iou_threshold: float = Field(0.45, description="NMS IOU 阈值")
    device: str = Field("cpu", description="运行设备")

async def rtdetr_detect(request: RTDETRDetectRequest):
    """RT-DETR 目标检测"""
    try:
        # 加载 RT-DETR 模型
        model = await model_manager.load_model(request.model_name, request.device)
        if not model:
            return {"success": False, "error": f"RT-DETR 模型加载失败: {request.model_name}"}
        
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            return {"success": False, "error": "图片不存在"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 执行检测
        result = model.predict(image, conf_threshold=request.conf_threshold, iou_threshold=request.iou_threshold)
        
        return {
            "success": True,
            "detections": result.get("detections", [])
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


# ==================== 格式导出 API ====================

class ExportRequest(BaseModel):
    format: str = Field(..., description="导出格式: yolo, voc, coco, dota, mask, all")
    subset: str = Field("train", description="数据集子集 (仅 YOLO 使用)")

async def export_annotations(request: ExportRequest, background_tasks: BackgroundTasks):
    """导出标注格式"""
    try:
        from export_utils import AnnotationExporter, export_all_formats
        
        output_dir = ANNOTATIONS_DIR.parent / "export"
        
        if request.format == "all":
            # 导出所有格式
            background_tasks.add_task(
                export_all_formats,
                IMAGES_DIR,
                ANNOTATIONS_DIR,
                output_dir
            )
            return {
                "success": True,
                "message": "正在导出所有格式，请稍候...",
                "output_dir": str(output_dir)
            }
        else:
            # 导出指定格式
            exporter = AnnotationExporter(IMAGES_DIR, ANNOTATIONS_DIR, output_dir)
            
            if request.format == "yolo":
                exporter.export_yolo(request.subset)
            elif request.format == "voc":
                exporter.export_voc()
            elif request.format == "coco":
                exporter.export_coco()
            elif request.format == "dota":
                exporter.export_dota()
            elif request.format == "mask":
                exporter.export_mask()
            else:
                return {"success": False, "error": f"不支持的格式: {request.format}"}
            
            return {
                "success": True,
                "message": f"{request.format.upper()} 格式导出完成",
                "output_dir": str(output_dir / request.format)
            }
    except Exception as e:
        return {"success": False, "error": str(e)}

# ==================== 图像分类 API ====================

class ClassificationRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    model_name: str = Field("yolov8n-cls", description="分类模型名称")
    top_k: int = Field(5, description="返回前 K 个结果")
    device: str = Field("cpu", description="运行设备")

async def classify_image(request: ClassificationRequest):
    """图像分类"""
    try:
        # 加载分类模型
        model = await model_manager.load_model(request.model_name, request.device)
        if not model:
            return {"success": False, "error": f"分类模型加载失败: {request.model_name}"}
        
        # 读取图片
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            return {"success": False, "error": "图片不存在"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 执行分类
        result = model.predict(image, top_k=request.top_k)
        
        return {
            "success": True,
            "predictions": result.get("predictions", []),
            "class_names": result.get("class_names", [])
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


# ==================== 跟踪 API ====================

class TrackingRequest(BaseModel):
    image_name: str = Field(..., description="图片名称")
    detections: List[Dict] = Field(..., description="检测结果列表")
    tracker_name: str = Field("bytetrack", description="跟踪器名称")
    device: str = Field("cpu", description="运行设备")
    reset: bool = Field(False, description="是否重置跟踪器")

async def track_objects(request: TrackingRequest):
    """目标跟踪"""
    try:
        # 加载跟踪器
        tracker = await model_manager.load_model(request.tracker_name, request.device)
        if not tracker:
            return {"success": False, "error": f"跟踪器加载失败: {request.tracker_name}"}
        
        # 如果需要重置跟踪器
        if request.reset:
            tracker.reset()
        
        # 读取图片（用于计算坐标）
        image_path = IMAGES_DIR / request.image_name
        if not image_path.exists():
            return {"success": False, "error": "图片不存在"}
        
        image = cv2.imread(str(image_path))
        if image is None:
            return {"success": False, "error": "图片读取失败"}
        
        # 更新跟踪
        tracked_results = tracker.update(image, request.detections)

        return {
            "success": True,
            "tracked_objects": tracked_results
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


# ==================== GroundingDINO 文本提示检测 API ====================

class GroundingDINORequest(BaseModel):
    """GroundingDINO 检测请求"""
    image_name: str = Field(..., description="图片文件名")
    text_prompt: str = Field(..., description="文本提示，如 'cat . dog . person'")
    model_name: str = Field(default="groundingdino-swinb", description="模型名称")
    box_threshold: float = Field(default=0.35, description="边界框置信度阈值")
    text_threshold: float = Field(default=0.25, description="文本匹配置信度阈值")

async def grounding_dino_detect(request: GroundingDINORequest):
    """
    使用 GroundingDINO 进行文本提示检测
    
    支持的文本格式:
    - "cat . dog . person" (推荐，用 . 分隔)
    - "cat, dog, person" (自动转换)
    - "cat dog person" (自动转换)
    """
    try:
        from models.grounding_dino_models import detect_with_text
        from PIL import Image
        import os
        
        # 检查图片是否存在
        image_path = IMAGES_DIR / request.image_name
        if not os.path.exists(image_path):
            return {
                "success": False,
                "error": f"图片不存在: {request.image_name}"
            }
        
        # 加载图片
        image = Image.open(image_path).convert("RGB")
        
        # 执行检测
        results = detect_with_text(
            image=image,
            text_prompt=request.text_prompt,
            model_name=request.model_name,
            box_threshold=request.box_threshold,
            text_threshold=request.text_threshold
        )
        
        # 转换结果格式为标注格式
        bboxes = []
        for result in results:
            # result["bbox"] 是 [x1, y1, x2, y2] 归一化坐标
            x1, y1, x2, y2 = result["bbox"]
            cx = (x1 + x2) / 2
            cy = (y1 + y2) / 2
            w = x2 - x1
            h = y2 - y1
            
            bboxes.append({
                "x": cx,
                "y": cy,
                "width": w,
                "height": h,
                "class_name": result["label"],
                "confidence": result["confidence"],
                "annotation_type": "bbox"
            })
        
        return {
            "success": True,
            "bboxes": bboxes,
            "count": len(bboxes),
            "prompt": request.text_prompt
        }
        
    except ImportError as e:
        return {
            "success": False,
            "error": "GroundingDINO 未安装，请运行: pip install groundingdino-py"
        }
    except Exception as e:
        import traceback
        return {
            "success": False,
            "error": f"检测失败: {str(e)}",
            "traceback": traceback.format_exc()
        }


class BatchGroundingDINORequest(BaseModel):
    """批量 GroundingDINO 检测请求"""
    image_names: List[str] = Field(..., description="图片文件名列表")
    text_prompt: str = Field(..., description="文本提示")
    model_name: str = Field(default="groundingdino-swinb", description="模型名称")
    box_threshold: float = Field(default=0.35, description="边界框置信度阈值")
    text_threshold: float = Field(default=0.25, description="文本匹配置信度阈值")
    save_annotations: bool = Field(default=True, description="是否保存标注")

async def batch_grounding_dino_detect(request: BatchGroundingDINORequest):
    """
    批量 GroundingDINO 检测
    """
    try:
        from models.grounding_dino_models import get_grounding_dino_model
        from PIL import Image
        import os
        
        # 获取模型实例
        model = get_grounding_dino_model(request.model_name)
        if not model.load_model():
            return {
                "success": False,
                "error": "模型加载失败"
            }
        
        results = []
        success_count = 0
        
        for image_name in request.image_names:
            try:
                # 检查图片是否存在
                image_path = IMAGES_DIR / image_name
                if not os.path.exists(image_path):
                    results.append({
                        "image_name": image_name,
                        "success": False,
                        "error": "图片不存在"
                    })
                    continue
                
                # 加载图片
                image = Image.open(image_path).convert("RGB")
                
                # 执行检测
                detections = model.predict(
                    image=image,
                    text_prompt=request.text_prompt,
                    box_threshold=request.box_threshold,
                    text_threshold=request.text_threshold
                )
                
                # 转换结果格式
                bboxes = []
                for det in detections:
                    x1, y1, x2, y2 = det["bbox"]
                    cx = (x1 + x2) / 2
                    cy = (y1 + y2) / 2
                    w = x2 - x1
                    h = y2 - y1
                    
                    bboxes.append({
                        "x": cx,
                        "y": cy,
                        "width": w,
                        "height": h,
                        "class_name": det["label"],
                        "confidence": det["confidence"],
                        "annotation_type": "bbox"
                    })
                
                # 保存标注
                if request.save_annotations and bboxes:
                    annotation_path = ANNOTATIONS_DIR / f"{os.path.splitext(image_name)[0]}.json"
                    
                    # 读取现有标注
                    if os.path.exists(annotation_path):
                        with open(annotation_path, 'r', encoding='utf-8') as f:
                            annotation_data = json.load(f)
                    else:
                        annotation_data = {
                            "image_name": image_name,
                            "bboxes": []
                        }
                    
                    # 添加新标注
                    annotation_data["bboxes"].extend(bboxes)
                    
                    # 保存
                    with open(annotation_path, 'w', encoding='utf-8') as f:
                        json.dump(annotation_data, f, ensure_ascii=False, indent=2)
                
                results.append({
                    "image_name": image_name,
                    "success": True,
                    "count": len(bboxes),
                    "bboxes": bboxes
                })
                success_count += 1
                
            except Exception as e:
                results.append({
                    "image_name": image_name,
                    "success": False,
                    "error": str(e)
                })
        
        return {
            "success": True,
            "results": results,
            "total": len(request.image_names),
            "success_count": success_count,
            "prompt": request.text_prompt
        }
        
    except Exception as e:
        import traceback
        return {
            "success": False,
            "error": f"批量检测失败: {str(e)}",
            "traceback": traceback.format_exc()
        }
