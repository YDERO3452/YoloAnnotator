using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace YoloAnnotator.Models
{
    public class BoundingBox
    {
        [JsonProperty("x")]
        [JsonPropertyName("x")]
        public float X { get; set; }  // 中心点 x (0-1)
        [JsonProperty("y")]
        [JsonPropertyName("y")]
        public float Y { get; set; }  // 中心点 y (0-1)
        [JsonProperty("width")]
        [JsonPropertyName("width")]
        public float Width { get; set; }  // 宽度 (0-1)
        [JsonProperty("height")]
        [JsonPropertyName("height")]
        public float Height { get; set; }  // 高度 (0-1)
        [JsonProperty("class_id")]
        [JsonPropertyName("class_id")]
        public int ClassId { get; set; }  // 类别 ID
        [JsonProperty("class_name")]
        [JsonPropertyName("class_name")]
        public string ClassName { get; set; } = string.Empty;
        [JsonProperty("confidence")]
        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }  // 置信度 (用于推理结果)
        [JsonProperty("annotation_type")]
        [JsonPropertyName("annotation_type")]
        public string AnnotationType { get; set; } = "bbox";  // 标注类型: bbox, obb, polygon, keypoints, circle, line, point, text
        [JsonProperty("angle")]
        [JsonPropertyName("angle")]
        public float Angle { get; set; } = 0f;  // 旋转角度（弧度，仅 OBB 使用）
        [JsonProperty("points")]
        [JsonPropertyName("points")]
        public List<List<float>> Points { get; set; } = new List<List<float>>();  // 多边形顶点列表 [[x1, y1], [x2, y2], ...]（仅分割使用）
        [JsonProperty("keypoints")]
        [JsonPropertyName("keypoints")]
        public List<List<float>> Keypoints { get; set; } = new List<List<float>>();  // 关键点列表 [[x1, y1], [x2, y2], ...]（仅姿态估计使用）
        [JsonProperty("color")]
        [JsonPropertyName("color")]
        public string? Color { get; set; }  // 自定义颜色（十六进制格式，如 "#22c55e"）
        
        // ===== 新增：支持 X-AnyLabeling 的额外标注类型 =====
        
        // 圆形标注
        [JsonProperty("radius")]
        [JsonPropertyName("radius")]
        public float Radius { get; set; } = 0f;  // 圆形半径 (0-1，相对于图片宽度)
        
        // 线条标注（使用 Points 存储起点和终点）
        
        // 点标注（使用 Points 存储）
        
        // 通用属性（支持自定义属性）
        [JsonProperty("attributes")]
        [JsonPropertyName("attributes")]
        public Dictionary<string, object>? Attributes { get; set; }  // 自定义属性字典
        
        // 标注元数据
        [JsonProperty("group_id")]
        [JsonPropertyName("group_id")]
        public int? GroupId { get; set; }  // 分组 ID（用于相关标注）
        [JsonProperty("description")]
        [JsonPropertyName("description")]
        public string? Description { get; set; }  // 标注描述
        [JsonProperty("difficult")]
        [JsonPropertyName("difficult")]
        public bool Difficult { get; set; } = false;  // 是否为困难样本
        [JsonProperty("flags")]
        [JsonPropertyName("flags")]
        public Dictionary<string, bool>? Flags { get; set; }  // 标注标志位
        
        // 标注 ID（唯一标识）
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public string? Id { get; set; }  // 标注唯一 ID
    }

    public class AnnotationData
    {
        [JsonProperty("version")]
        [JsonPropertyName("version")]
        public string Version { get; set; } = "5.5.0";  // 标注格式版本
        
        [JsonProperty("flags")]
        [JsonPropertyName("flags")]
        public Dictionary<string, object> Flags { get; set; } = new Dictionary<string, object>();
        
        [JsonProperty("shapes")]
        [JsonPropertyName("shapes")]
        public List<BoundingBox> Shapes { get; set; } = new List<BoundingBox>();  // 使用 shapes 替代 bboxes，兼容 X-AnyLabeling 格式
        
        [JsonProperty("image_name")]
        [JsonPropertyName("image_name")]
        public string ImageName { get; set; } = string.Empty;
        
        [JsonProperty("width")]
        [JsonPropertyName("width")]
        public int Width { get; set; }
        
        [JsonProperty("height")]
        [JsonPropertyName("height")]
        public int Height { get; set; }
        
        [JsonProperty("bboxes")]
        [JsonPropertyName("bboxes")]
        public List<BoundingBox> Bboxes { get; set; } = new List<BoundingBox>();  // 向后兼容
        
        [JsonProperty("description")]
        [JsonPropertyName("description")]
        public string? Description { get; set; }  // 图像描述
        
        [JsonProperty("chat_history")]
        [JsonPropertyName("chat_history")]
        public List<ChatMessage>? ChatHistory { get; set; }  // 聊天历史（VQA 功能）
        
        [JsonProperty("vqaData")]
        [JsonPropertyName("vqaData")]
        public VQAData? VQAData { get; set; }  // 视觉问答数据
        
        [JsonProperty("imagePath")]
        [JsonPropertyName("imagePath")]
        public string? ImagePath { get; set; }  // 图像路径
        
        [JsonProperty("imageData")]
        [JsonPropertyName("imageData")]
        public string? ImageData { get; set; }  // 图像数据（base64）
        
        // 兼容性属性：自动同步 shapes 和 bboxes
        public List<BoundingBox> GetShapes()
        {
            return Shapes.Count > 0 ? Shapes : Bboxes;
        }
        
        public void SetShapes(List<BoundingBox> shapes)
        {
            Shapes = shapes;
            Bboxes = shapes;  // 向后兼容
        }
    }
    
    // 聊天消息（用于 VQA 功能）
    public class ChatMessage
    {
        [JsonProperty("role")]
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;  // "user" 或 "assistant"
        
        [JsonProperty("content")]
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;  // 消息内容
        
        [JsonProperty("timestamp")]
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }  // 时间戳
    }
    
    // 视觉问答数据
    public class VQAData
    {
        [JsonProperty("questions")]
        [JsonPropertyName("questions")]
        public List<string> Questions { get; set; } = new List<string>();
        
        [JsonProperty("answers")]
        [JsonPropertyName("answers")]
        public List<string> Answers { get; set; } = new List<string>();
        
        [JsonProperty("model_name")]
        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }  // 使用的模型名称
    }

    public class ImageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool HasAnnotation { get; set; }
    }

    public class TrainRequest
    {
        [JsonProperty("epochs")]
        [JsonPropertyName("epochs")]
        public int Epochs { get; set; } = 100;
        [JsonProperty("batch_size")]
        [JsonPropertyName("batch_size")]
        public int BatchSize { get; set; } = 16;
        [JsonProperty("image_size")]
        [JsonPropertyName("image_size")]
        public int ImageSize { get; set; } = 640;
        [JsonProperty("device")]
        [JsonPropertyName("device")]
        public string Device { get; set; } = "0";
        [JsonProperty("classes")]
        [JsonPropertyName("classes")]
        public List<string> Classes { get; set; } = new List<string>();
        [JsonProperty("model_type")]
        [JsonPropertyName("model_type")]
        public string ModelType { get; set; } = "yolov8n";
        [JsonProperty("weights_path")]
        [JsonPropertyName("weights_path")]
        public string WeightsPath { get; set; } = "";
        [JsonProperty("task_type")]
        [JsonPropertyName("task_type")]
        public string TaskType { get; set; } = "detection";  // 任务类型: detection, segmentation, obb
    }

    public class DetectionResult
    {
        [JsonProperty("bboxes")]
        [JsonPropertyName("bboxes")]
        public List<BoundingBox> Bboxes { get; set; } = new List<BoundingBox>();
        [JsonProperty("classes")]
        [JsonPropertyName("classes")]
        public List<string> Classes { get; set; } = new List<string>();
        [JsonProperty("confidences")]
        [JsonPropertyName("confidences")]
        public List<float> Confidences { get; set; } = new List<float>();
    }
}