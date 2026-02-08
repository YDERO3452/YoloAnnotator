"""
格式导出工具 - 支持多种标注格式导出
"""
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import List, Dict, Any, Optional
import shutil
import numpy as np
from PIL import Image
import cv2  # 添加 cv2 导入


class AnnotationExporter:
    """标注格式导出器"""
    
    def __init__(self, images_dir: Path, annotations_dir: Path, output_dir: Path):
        """
        初始化导出器
        
        Args:
            images_dir: 图片目录
            annotations_dir: 标注目录
            output_dir: 输出目录
        """
        self.images_dir = images_dir
        self.annotations_dir = annotations_dir
        self.output_dir = output_dir
        self.output_dir.mkdir(parents=True, exist_ok=True)
    
    def export_yolo(self, subset: str = "train"):
        """
        导出 YOLO 格式
        
        Args:
            subset: 数据集子集 (train, val, test)
        """
        print(f"[EXPORT] 导出 YOLO 格式: {subset}")
        
        # 创建 YOLO 目录结构
        yolo_dir = self.output_dir / "yolo"
        images_dir = yolo_dir / subset / "images"
        labels_dir = yolo_dir / subset / "labels"
        
        images_dir.mkdir(parents=True, exist_ok=True)
        labels_dir.mkdir(parents=True, exist_ok=True)
        
        # 获取所有标注文件
        annotation_files = list(self.annotations_dir.glob("*.json"))
        
        # 提取所有类别
        all_classes = set()
        for ann_file in annotation_files:
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
                for shape in data.get("shapes", []):
                    label = shape.get("label", "")
                    if label:
                        all_classes.add(label)
        
        class_to_id = {name: idx for idx, name in enumerate(sorted(all_classes))}
        
        # 转换标注
        for ann_file in annotation_files:
            image_name = ann_file.stem
            image_path = self.images_dir / image_name
            
            if not image_path.exists():
                continue
            
            # 复制图片
            shutil.copy(image_path, images_dir / image_path.name)
            
            # 转换标注
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
            
            yolo_labels = []
            img_width = data.get("width", 1)
            img_height = data.get("height", 1)
            
            for shape in data.get("shapes", []):
                label = shape.get("label", "")
                shape_type = shape.get("shape_type", "rectangle")
                
                if label not in class_to_id:
                    continue
                
                class_id = class_to_id[label]
                
                if shape_type == "rectangle":
                    # 矩形框
                    points = shape.get("points", [])
                    if len(points) >= 2:
                        x1, y1 = points[0]
                        x2, y2 = points[1]
                        x_center = (x1 + x2) / 2 / img_width
                        y_center = (y1 + y2) / 2 / img_height
                        width = abs(x2 - x1) / img_width
                        height = abs(y2 - y1) / img_height
                        yolo_labels.append(f"{class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}")
                
                elif shape_type == "polygon":
                    # 多边形
                    points = shape.get("points", [])
                    if len(points) >= 3:
                        points_str = " ".join([f"{p[0]/img_width:.6f} {p[1]/img_height:.6f}" for p in points])
                        yolo_labels.append(f"{class_id} {points_str}")
            
            # 保存标签文件
            label_file = labels_dir / f"{image_name}.txt"
            label_file.write_text("\n".join(yolo_labels))
        
        # 创建 data.yaml
        data_yaml = yolo_dir / "data.yaml"
        names_dict = {idx: name for name, idx in class_to_id.items()}
        yaml_content = f"""path: {yolo_dir.absolute()}
train: train/images
val: val/images
nc: {len(class_to_id)}
names: {names_dict}
"""
        data_yaml.write_text(yaml_content, encoding="utf-8")
        
        print(f"[EXPORT] YOLO 格式导出完成: {yolo_dir}")
    
    def export_voc(self):
        """
        导出 VOC 格式 (XML)
        """
        print(f"[EXPORT] 导出 VOC 格式")
        
        # 创建 VOC 目录结构
        voc_dir = self.output_dir / "VOCdevkit" / "VOC2007"
        images_dir = voc_dir / "JPEGImages"
        annotations_dir = voc_dir / "Annotations"
        
        images_dir.mkdir(parents=True, exist_ok=True)
        annotations_dir.mkdir(parents=True, exist_ok=True)
        
        # 获取所有标注文件
        annotation_files = list(self.annotations_dir.glob("*.json"))
        
        # 提取所有类别
        all_classes = set()
        for ann_file in annotation_files:
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
                for shape in data.get("shapes", []):
                    label = shape.get("label", "")
                    if label:
                        all_classes.add(label)
        
        # 转换标注
        for ann_file in annotation_files:
            image_name = ann_file.stem
            image_path = self.images_dir / image_name
            
            if not image_path.exists():
                continue
            
            # 复制图片
            shutil.copy(image_path, images_dir / image_path.name)
            
            # 读取图片尺寸
            with Image.open(image_path) as img:
                width, height = img.size
            
            # 读取标注
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
            
            # 创建 XML 根元素
            annotation = ET.Element("annotation")
            
            # 添加基本信息
            ET.SubElement(annotation, "filename").text = image_name + image_path.suffix
            ET.SubElement(annotation, "folder").text = "VOC2007"
            
            size = ET.SubElement(annotation, "size")
            ET.SubElement(size, "width").text = str(width)
            ET.SubElement(size, "height").text = str(height)
            ET.SubElement(size, "depth").text = "3"
            
            # 添加对象
            for shape in data.get("shapes", []):
                label = shape.get("label", "")
                shape_type = shape.get("shape_type", "rectangle")
                
                if shape_type != "rectangle":
                    continue
                
                obj = ET.SubElement(annotation, "object")
                ET.SubElement(obj, "name").text = label
                ET.SubElement(obj, "difficult").text = "0"
                
                points = shape.get("points", [])
                if len(points) >= 2:
                    x1, y1 = points[0]
                    x2, y2 = points[1]
                    
                    bndbox = ET.SubElement(obj, "bndbox")
                    ET.SubElement(bndbox, "xmin").text = str(int(min(x1, x2)))
                    ET.SubElement(bndbox, "ymin").text = str(int(min(y1, y2)))
                    ET.SubElement(bndbox, "xmax").text = str(int(max(x1, x2)))
                    ET.SubElement(bndbox, "ymax").text = str(int(max(y1, y2)))
            
            # 保存 XML 文件
            xml_file = annotations_dir / f"{image_name}.xml"
            tree = ET.ElementTree(annotation)
            ET.indent(tree, space="  ")
            tree.write(xml_file, encoding="utf-8", xml_declaration=True)
        
        print(f"[EXPORT] VOC 格式导出完成: {voc_dir}")
    
    def export_coco(self):
        """
        导出 COCO 格式 (JSON)
        """
        print(f"[EXPORT] 导出 COCO 格式")
        
        # 获取所有标注文件
        annotation_files = list(self.annotations_dir.glob("*.json"))
        
        # 提取所有类别
        all_classes = set()
        for ann_file in annotation_files:
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
                for shape in data.get("shapes", []):
                    label = shape.get("label", "")
                    if label:
                        all_classes.add(label)
        
        class_to_id = {name: idx for idx, name in enumerate(sorted(all_classes))}
        
        # 创建 COCO 数据结构
        coco_data = {
            "images": [],
            "annotations": [],
            "categories": [{"id": idx, "name": name} for name, idx in class_to_id.items()]
        }
        
        annotation_id = 1
        
        # 转换标注
        for image_id, ann_file in enumerate(annotation_files, start=1):
            image_name = ann_file.stem
            image_path = self.images_dir / image_name
            
            if not image_path.exists():
                continue
            
            # 读取图片尺寸
            with Image.open(image_path) as img:
                width, height = img.size
            
            # 添加图像信息
            coco_data["images"].append({
                "id": image_id,
                "file_name": image_name + image_path.suffix,
                "width": width,
                "height": height
            })
            
            # 读取标注
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
            
            # 添加标注
            for shape in data.get("shapes", []):
                label = shape.get("label", "")
                shape_type = shape.get("shape_type", "rectangle")
                
                if label not in class_to_id:
                    continue
                
                if shape_type != "rectangle":
                    continue
                
                points = shape.get("points", [])
                if len(points) >= 2:
                    x1, y1 = points[0]
                    x2, y2 = points[1]
                    
                    # COCO 格式使用 [x, y, width, height]
                    bbox = [
                        min(x1, x2),
                        min(y1, y2),
                        abs(x2 - x1),
                        abs(y2 - y1)
                    ]
                    
                    coco_data["annotations"].append({
                        "id": annotation_id,
                        "image_id": image_id,
                        "category_id": class_to_id[label],
                        "bbox": [round(x, 2) for x in bbox],
                        "area": round(bbox[2] * bbox[3], 2),
                        "iscrowd": 0
                    })
                    annotation_id += 1
        
        # 保存 COCO JSON 文件
        coco_file = self.output_dir / "coco_instances.json"
        with coco_file.open("w", encoding="utf-8") as f:
            json.dump(coco_data, f, indent=2, ensure_ascii=False)
        
        print(f"[EXPORT] COCO 格式导出完成: {coco_file}")
    
    def export_dota(self):
        """
        导出 DOTA 格式（旋转框）
        """
        print(f"[EXPORT] 导出 DOTA 格式")
        
        # 创建 DOTA 目录
        dota_dir = self.output_dir / "DOTA"
        images_dir = dota_dir / "images"
        labels_dir = dota_dir / "labelTxt"
        
        images_dir.mkdir(parents=True, exist_ok=True)
        labels_dir.mkdir(parents=True, exist_ok=True)
        
        # 获取所有标注文件
        annotation_files = list(self.annotations_dir.glob("*.json"))
        
        # 转换标注
        for ann_file in annotation_files:
            image_name = ann_file.stem
            image_path = self.images_dir / image_name
            
            if not image_path.exists():
                continue
            
            # 复制图片
            shutil.copy(image_path, images_dir / image_path.name)
            
            # 读取标注
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
            
            img_width = data.get("width", 1)
            img_height = data.get("height", 1)
            
            # DOTA 格式每行一个对象: x1 y1 x2 y2 x3 y3 x4 y4 class difficulty
            label_lines = []
            
            for shape in data.get("shapes", []):
                label = shape.get("label", "")
                shape_type = shape.get("shape_type", "rectangle")
                
                if shape_type == "rectangle":
                    points = shape.get("points", [])
                    if len(points) >= 2:
                        x1, y1 = points[0]
                        x2, y2 = points[1]
                        # 转换为四边形
                        x1, y1 = min(x1, x2), min(y1, y2)
                        x2, y2 = max(x1, x2), max(y1, y2)
                        # 创建四个顶点
                        coords = [
                            x1, y1,
                            x2, y1,
                            x2, y2,
                            x1, y2
                        ]
                        label_lines.append(f"{' '.join(map(str, coords))} {label} 0")
                elif shape_type == "polygon":
                    points = shape.get("points", [])
                    if len(points) >= 4:
                        coords = []
                        for p in points:
                            coords.extend([p[0], p[1]])
                        label_lines.append(f"{' '.join(map(str, coords))} {label} 0")
            
            # 保存标签文件
            label_file = labels_dir / f"{image_name}.txt"
            label_file.write_text("\n".join(label_lines))
        
        print(f"[EXPORT] DOTA 格式导出完成: {dota_dir}")
    
    def export_mask(self):
        """
        导出 Mask 格式（PNG 掩码）
        """
        print(f"[EXPORT] 导出 Mask 格式")
        
        # 创建 Mask 目录
        mask_dir = self.output_dir / "masks"
        mask_dir.mkdir(parents=True, exist_ok=True)
        
        # 获取所有标注文件
        annotation_files = list(self.annotations_dir.glob("*.json"))
        
        # 提取所有类别
        all_classes = set()
        for ann_file in annotation_files:
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
                for shape in data.get("shapes", []):
                    label = shape.get("label", "")
                    if label:
                        all_classes.add(label)
        
        class_to_id = {name: idx for idx, name in enumerate(sorted(all_classes))}
        
        # 转换标注
        for ann_file in annotation_files:
            image_name = ann_file.stem
            image_path = self.images_dir / image_name
            
            if not image_path.exists():
                continue
            
            # 读取图片尺寸
            with Image.open(image_path) as img:
                width, height = img.size
            
            # 读取标注
            with ann_file.open("r", encoding="utf-8") as f:
                data = json.load(f)
            
            # 创建空白掩码
            mask = np.zeros((height, width), dtype=np.uint8)
            
            # 绘制掩码
            for shape in data.get("shapes", []):
                label = shape.get("label", "")
                shape_type = shape.get("shape_type", "rectangle")
                
                if label not in class_to_id:
                    continue
                
                class_id = class_to_id[label] + 1  # 0 为背景
                
                if shape_type == "rectangle":
                    points = shape.get("points", [])
                    if len(points) >= 2:
                        x1, y1 = points[0]
                        x2, y2 = points[1]
                        x1, y1 = int(min(x1, x2)), int(min(y1, y2))
                        x2, y2 = int(max(x1, x2)), int(max(y1, y2))
                        mask[y1:y2, x1:x2] = class_id
                
                elif shape_type == "polygon":
                    points = shape.get("points", [])
                    if len(points) >= 3:
                        # 创建多边形掩码
                        poly_points = np.array([(int(p[0]), int(p[1])) for p in points], np.int32)
                        poly_mask = np.zeros((height, width), dtype=np.uint8)
                        cv2.fillPoly(poly_mask, [poly_points], class_id)
                        mask = np.maximum(mask, poly_mask)
            
            # 保存掩码
            mask_file = mask_dir / f"{image_name}.png"
            mask_image = Image.fromarray(mask)
            mask_image.save(mask_file)
        
        print(f"[EXPORT] Mask 格式导出完成: {mask_dir}")


def export_all_formats(images_dir: Path, annotations_dir: Path, output_dir: Path):
    """
    导出所有支持的格式
    
    Args:
        images_dir: 图片目录
        annotations_dir: 标注目录
        output_dir: 输出目录
    """
    exporter = AnnotationExporter(images_dir, annotations_dir, output_dir)
    
    # 导出各种格式
    exporter.export_yolo()
    exporter.export_voc()
    exporter.export_coco()
    exporter.export_dota()
    exporter.export_mask()
    
    print(f"[EXPORT] 所有格式导出完成: {output_dir}")


if __name__ == "__main__":
    # 测试导出功能
    base_dir = Path(__file__).parent
    images_dir = base_dir / "images"
    annotations_dir = base_dir / "annotations"
    output_dir = base_dir / "export"
    
    export_all_formats(images_dir, annotations_dir, output_dir)
