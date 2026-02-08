using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;
using YoloAnnotator.Models;

namespace YoloAnnotator.Services
{
    public class ApiService
    {
        private readonly string _baseUrl = "http://localhost:8000/api";
        private readonly RestClient _client;

        public ApiService()
        {
            // 使用默认配置（System.Text.Json），因为我们在模型中添加了 [JsonPropertyName] 属性
            _client = new RestClient(_baseUrl);
        }

        // ==================== 后端检查 ====================

        public async Task<bool> CheckBackendAsync()
        {
            try
            {
                // 使用带超时的健康检查
                var request = new RestRequest("/health", Method.Get);
                request.Timeout = TimeSpan.FromMilliseconds(3000); // 3秒超时

                var response = await _client.ExecuteAsync(request);

                // 检查响应是否成功且包含预期的内容
                if (response.IsSuccessful && response.Content != null)
                {
                    var content = response.Content;
                    return content.Contains("\"status\":\"ok\"") || content.Contains("YOLO Annotator Backend");
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"后端检查失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 图片管理 ====================

        public async Task<List<string>> GetImagesAsync()
        {
            try
            {
                var request = new RestRequest("/images");
                var response = await _client.GetAsync<ImageListResponse>(request);
                return response?.Images ?? new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取图片列表失败: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<bool> UploadImageAsync(string filePath)
        {
            try
            {
                Console.WriteLine($"[DEBUG] 开始上传文件: {filePath}");

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[ERROR] 文件不存在: {filePath}");
                    return false;
                }

                var fileInfo = new System.IO.FileInfo(filePath);
                var fileName = System.IO.Path.GetFileName(filePath);

                Console.WriteLine($"[DEBUG] 文件信息: 名称={fileName}, 大小={fileInfo.Length} 字节, 扩展名={fileInfo.Extension}");

                var request = new RestRequest("/images/upload", Method.Post);
                request.AddFile("file", filePath);

                Console.WriteLine($"[DEBUG] 发送请求到: {_baseUrl}/images/upload");

                var response = await _client.ExecuteAsync(request);

                Console.WriteLine($"[DEBUG] 响应状态: {response.StatusCode}, 内容: {response.Content}");

                if (!response.IsSuccessful)
                {
                    Console.WriteLine($"[ERROR] 上传失败: {response.StatusCode} - {response.Content}");
                    return false;
                }

                Console.WriteLine($"[SUCCESS] 上传成功: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 上传图片异常: {ex.Message}");
                Console.WriteLine($"[ERROR] 异常详情: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<string?> GetImageBase64Async(string imageName)
        {
            try
            {
                var request = new RestRequest($"/images/{Uri.EscapeDataString(imageName)}");
                var response = await _client.GetAsync<ImageResponse>(request);
                return response?.Image;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取图片失败: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeleteImageAsync(string imageName)
        {
            try
            {
                var request = new RestRequest($"/images/{Uri.EscapeDataString(imageName)}", Method.Delete);
                var response = await _client.ExecuteAsync(request);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除图片失败: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> GetAvailableModelsAsync()
        {
            try
            {
                var request = new RestRequest("/models/available");
                var response = await _client.GetAsync<AvailableModelsResponse>(request);
                return response?.Models ?? new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取可用模型列表失败: {ex.Message}");
                return new List<string>();
            }
        }

        // ==================== 高级检测功能 ====================

        public async Task<PoseDetectResult?> DetectPoseAsync(string imageName, float confidence = 0.25f)
        {
            try
            {
                var request = new RestRequest("/detect/pose", Method.Post);
                request.AddJsonBody(new { image_name = imageName, conf = confidence });
                var response = await _client.ExecuteAsync<PoseDetectResponse>(request);
                return response?.Data?.Poses != null ? new PoseDetectResult { Poses = response.Data.Poses, Count = response.Data.Count } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"姿态检测失败: {ex.Message}");
                return null;
            }
        }

        public async Task<SegmentDetectResult?> DetectSegmentationAsync(string imageName, float confidence = 0.25f)
        {
            try
            {
                var request = new RestRequest("/detect/segment", Method.Post);
                request.AddJsonBody(new { image_name = imageName, conf = confidence });
                var response = await _client.ExecuteAsync<SegmentDetectResponse>(request);
                return response?.Data?.Segments != null ? new SegmentDetectResult { Segments = response.Data.Segments, Count = response.Data.Count } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"分割检测失败: {ex.Message}");
                return null;
            }
        }

        public async Task<LicensePlateResult?> DetectLicensePlateAsync(string imageName, float confidence = 0.3f)
        {
            try
            {
                var request = new RestRequest("/detect/license-plate", Method.Post);
                request.AddJsonBody(new { image_name = imageName, conf = confidence });
                var response = await _client.ExecuteAsync<LicensePlateResponse>(request);
                return response?.Data?.Plates != null ? new LicensePlateResult { Plates = response.Data.Plates, Count = response.Data.Count } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"车牌检测失败: {ex.Message}");
                return null;
            }
        }

        // ==================== OpenCV 更多功能 ====================

        public async Task<FaceDetectResult?> OpencvFaceDetectAsync(string imageName, float scaleFactor = 1.1f, int minNeighbors = 3)
        {
            try
            {
                var request = new RestRequest("/opencv/face-detect", Method.Post);
                request.AddJsonBody(new { image_name = imageName, scale_factor = scaleFactor, min_neighbors = minNeighbors });
                var response = await _client.ExecuteAsync<FaceDetectResponse>(request);
                return response?.Data?.Faces != null ? new FaceDetectResult { Faces = response.Data.Faces, Count = response.Data.Count } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"人脸检测失败: {ex.Message}");
                return null;
            }
        }

        public async Task<LaneDetectResult?> DetectLaneAsync(string imageName, int cannyThreshold1 = 50, int cannyThreshold2 = 150)
        {
            try
            {
                var request = new RestRequest("/realtime/lane-detect", Method.Post);
                request.AddJsonBody(new { image_name = imageName, canny_threshold1 = cannyThreshold1, canny_threshold2 = cannyThreshold2 });
                var response = await _client.ExecuteAsync<LaneDetectResponse>(request);
                return response?.Data != null ? new LaneDetectResult 
                { 
                    LaneLines = response.Data.LaneLines, 
                    Count = response.Data.Count 
                } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"车道线检测失败: {ex.Message}");
                return null;
            }
        }

        public async Task<HandDetectResult?> DetectHandAsync(string imageName)
        {
            try
            {
                var request = new RestRequest("/realtime/hand-detect", Method.Post);
                request.AddJsonBody(new { image_name = imageName });
                var response = await _client.ExecuteAsync<HandDetectResponse>(request);
                return response?.Data != null ? new HandDetectResult 
                { 
                    Hands = response.Data.Hands, 
                    Count = response.Data.Count 
                } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"手部检测失败: {ex.Message}");
                return null;
            }
        }

        // ==================== 摄像头功能 ====================

        public async Task<CameraInitResult?> InitCameraAsync(int cameraId = 0, int width = 640, int height = 480)
        {
            try
            {
                var request = new RestRequest("/camera/init", Method.Post);
                request.AddJsonBody(new { camera_id = cameraId, width, height });
                var response = await _client.ExecuteAsync<CameraInitResponse>(request);
                return response?.Data != null ? new CameraInitResult 
                { 
                    Success = response.Data.Success, 
                    Message = response.Data.Message,
                    Width = response.Data.Width,
                    Height = response.Data.Height
                } : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"摄像头初始化失败: {ex.Message}");
                return null;
            }
        }

        public async Task<CameraFrameResult?> GetCameraFrameAsync(string operation)
        {
            try
            {
                var request = new RestRequest("/camera/frame", Method.Post);
                request.AddJsonBody(new { operation });
                var response = await _client.ExecuteAsync<CameraFrameResponse>(request);
                
                if (response?.Data == null)
                    return null;
                
                var data = response.Data;
                
                return new CameraFrameResult 
                { 
                    Success = data.Success,
                    Width = data.Width,
                    Height = data.Height,
                    Operation = data.Operation,
                    Image = data.Image,
                    Faces = data.Faces,
                    FaceCount = data.Faces?.Count ?? 0,
                    LaneLines = data.LaneLines,
                    LaneCount = data.LaneLines?.Count ?? 0,
                    Hands = data.Hands,
                    HandCount = data.Hands?.Count ?? 0
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取摄像头帧失败: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> StopCameraAsync()
        {
            try
            {
                var request = new RestRequest("/camera/stop", Method.Post);
                var response = await _client.ExecuteAsync<CameraStopResponse>(request);
                return response?.Data?.Success ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭摄像头失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 标注管理 ====================

        public async Task<AnnotationData?> GetAnnotationAsync(string imageName)
        {
            try
            {
                var request = new RestRequest($"/annotations/{Uri.EscapeDataString(imageName)}");
                var response = await _client.GetAsync<AnnotationData>(request);
                
                if (response?.Bboxes != null)
                {
                    foreach (var bbox in response.Bboxes)
                    {
                        Console.WriteLine($"[DEBUG] 加载标注框: class_id={bbox.ClassId}, class_name={bbox.ClassName}");
                    }
                }
                
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取标注失败: {ex.Message}");
                return new AnnotationData { ImageName = imageName, Bboxes = new List<BoundingBox>() };
            }
        }

        public async Task<bool> SaveAnnotationAsync(string imageName, AnnotationData annotation)
        {
            try
            {
                if (annotation == null)
                {
                    Console.WriteLine($"[ERROR] 标注数据为null");
                    return false;
                }

                Console.WriteLine($"[DEBUG] 保存标注: 图片={imageName}, 边界框数量={annotation?.Bboxes?.Count ?? 0}");
                Console.WriteLine($"[DEBUG] 标注数据: 宽度={annotation?.Width}, 高度={annotation?.Height}");
                
                if (annotation?.Bboxes != null)
                {
                    for (int i = 0; i < annotation.Bboxes.Count; i++)
                    {
                        var bbox = annotation.Bboxes[i];
                        Console.WriteLine($"[DEBUG] 标注框[{i}]: class_id={bbox.ClassId}, class_name={bbox.ClassName}");
                    }
                }

                var request = new RestRequest($"/annotations/{Uri.EscapeDataString(imageName)}", Method.Post);
                request.AddJsonBody(annotation!);
                var response = await _client.ExecuteAsync(request);

                Console.WriteLine($"[DEBUG] 保存标注响应: {response.StatusCode}, 内容={response.Content}");

                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 保存标注失败: {ex.Message}");
                Console.WriteLine($"[ERROR] 异常详情: {ex.StackTrace}");
                return false;
            }
        }

        // ==================== 模型训练 ====================

        public async Task<bool> CheckCudaAvailableAsync()
        {
            try
            {
                var request = new RestRequest("/check-cuda");
                var response = await _client.GetAsync<CudaCheckResponse>(request);
                return response?.Available ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查CUDA失败: {ex.Message}");
                return false;
            }
        }

        public async Task<CudaInfo?> GetCudaInfoAsync()
        {
            try
            {
                var request = new RestRequest("/check-cuda");
                var response = await _client.ExecuteAsync(request);
                
                Console.WriteLine($"[DEBUG] CUDA原始响应: {response.Content}");
                
                if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                {
                    Console.WriteLine($"[DEBUG] CUDA请求失败: {response.StatusCode}");
                    return new CudaInfo { Available = false, Reason = $"请求失败: {response.StatusCode}" };
                }
                
                var checkResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<CudaCheckResponse>(response.Content);
                
                Console.WriteLine($"[DEBUG] CUDA解析结果: Available={checkResponse?.Available}, DeviceName={checkResponse?.DeviceName}, DeviceCount={checkResponse?.DeviceCount}, GpuMemoryGb={checkResponse?.GpuMemoryGb}");
                
                // 总是返回CudaInfo，即使不可用
                return new CudaInfo
                {
                    Available = checkResponse?.Available ?? false,
                    PyTorchVersion = checkResponse?.PyTorchVersion,
                    CudaVersion = checkResponse?.CudaVersion,
                    DeviceName = checkResponse?.DeviceName,
                    DeviceCount = checkResponse?.DeviceCount ?? 0,
                    GpuMemoryGb = checkResponse?.GpuMemoryGb,
                    DriverVersion = checkResponse?.DriverVersion,
                    ComputeCapability = checkResponse?.ComputeCapability,
                    System = checkResponse?.System,
                    PythonVersion = checkResponse?.PythonVersion,
                    Reason = checkResponse?.Reason
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取CUDA信息失败: {ex.Message}");
                return new CudaInfo { Available = false, Reason = ex.Message };
            }
        }

        public async Task<bool> StartTrainingAsync(TrainRequest request)
        {
            try
            {
                var restRequest = new RestRequest("/train", Method.Post);
                restRequest.AddJsonBody(request);
                var response = await _client.ExecuteAsync(restRequest);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动训练失败: {ex.Message}");
                return false;
            }
        }

        // ==================== 推理检测 ====================

        public async Task<List<string>?> GetModelClassesAsync()
        {
            try
            {
                var request = new RestRequest("/model-classes");
                var response = await _client.GetAsync<ModelClassesResponse>(request);
                return response?.Classes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取模型类别失败: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<bool> ReloadModelAsync()
        {
            try
            {
                var request = new RestRequest("/reload-model", Method.Post);
                var response = await _client.ExecuteAsync(request);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重新加载模型失败: {ex.Message}");
                return false;
            }
        }

        public async Task<List<BoundingBox>?> DetectObjectsAsync(string imageName, List<string>? modelClasses = null)
        {
            try
            {
                var request = new RestRequest($"/detect?image_name={Uri.EscapeDataString(imageName)}", Method.Get);
                var response = await _client.GetAsync<DetectResponse>(request);
                
                // 如果提供了模型类别映射，仅在后端返回的 class_name 为空时使用它来设置类别名称
                if (response?.Detections != null && modelClasses != null)
                {
                    foreach (var detection in response.Detections)
                    {
                        // 如果后端返回了类别名称，优先使用后端的
                        if (string.IsNullOrEmpty(detection.ClassName))
                        {
                            if (detection.ClassId >= 0 && detection.ClassId < modelClasses.Count)
                            {
                                detection.ClassName = modelClasses[detection.ClassId];
                            }
                            else
                            {
                                detection.ClassName = "unknown";
                            }
                        }
                    }
                }
                
                return response?.Detections;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测失败: {ex.Message}");
                return new List<BoundingBox>();
            }
        }

        // ==================== 模型管理 ====================

        public async Task<DownloadResult> DownloadImagesFromUrlAsync(string url, int count, double delay)
        {
            try
            {
                var request = new RestRequest("/download/from-url", Method.Post);
                request.AddJsonBody(new
                {
                    url = url,
                    count = count,
                    delay = delay
                });

                var response = await _client.PostAsync<DownloadResponse>(request);

                if (response != null)
                {
                    return new DownloadResult
                    {
                        Success = response.Success,
                        DownloadedCount = response.DownloadedCount,
                        FailedCount = response.FailedCount,
                        ErrorMessage = response.ErrorMessage
                    };
                }

                return new DownloadResult { Success = false, ErrorMessage = "服务器无响应" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从网址下载图片失败: {ex.Message}");
                return new DownloadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<DownloadResult> DownloadImagesFromKeywordAsync(string keyword, int count, string size, double delay)
        {
            try
            {
                var request = new RestRequest("/download/from-keyword", Method.Post);
                request.AddJsonBody(new
                {
                    keyword = keyword,
                    count = count,
                    size = size,
                    delay = delay
                });

                var response = await _client.PostAsync<DownloadResponse>(request);

                if (response != null)
                {
                    return new DownloadResult
                    {
                        Success = response.Success,
                        DownloadedCount = response.DownloadedCount,
                        FailedCount = response.FailedCount,
                        ErrorMessage = response.ErrorMessage
                    };
                }

                return new DownloadResult { Success = false, ErrorMessage = "服务器无响应" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从关键词下载图片失败: {ex.Message}");
                return new DownloadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<bool> DeleteTrainingDataAsync()
        {
            try
            {
                var request = new RestRequest("/training-data", Method.Delete);
                var response = await _client.ExecuteAsync(request);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除训练数据失败: {ex.Message}");
                return false;
            }
        }

        public async Task<List<ModelInfo>?> GetModelsAsync()
        {
            try
            {
                var request = new RestRequest("/models");
                var response = await _client.GetAsync<ModelsResponse>(request);
                return response?.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取模型列表失败: {ex.Message}");
                return new List<ModelInfo>();
            }
        }

        // ==================== 模型配置管理 ====================

        public async Task<List<ModelConfigInfo>?> GetModelConfigsAsync()
        {
            try
            {
                var request = new RestRequest("/configs");
                var response = await _client.GetAsync<ModelConfigsResponse>(request);
                return response?.Configs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取模型配置失败: {ex.Message}");
                return new List<ModelConfigInfo>();
            }
        }

        public async Task<ModelConfigInfo?> GetModelConfigAsync(string modelName)
        {
            try
            {
                var request = new RestRequest($"/configs/{modelName}");
                var response = await _client.GetAsync<ModelConfigInfo>(request);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取模型配置失败: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> ReloadModelConfigsAsync()
        {
            try
            {
                var request = new RestRequest("/configs/reload", Method.Post);
                var response = await _client.ExecuteAsync(request);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重新加载模型配置失败: {ex.Message}");
                return false;
            }
        }

        public async Task<T?> PostAsync<T>(string endpoint, object data)
        {
            try
            {
                var request = new RestRequest(endpoint, Method.Post);
                request.AddJsonBody(data);
                var response = await _client.ExecuteAsync(request);
                
                if (response.IsSuccessful && response.Content != null)
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(response.Content);
                }
                
                return default(T);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"POST请求失败: {ex.Message}");
                return default(T);
            }
        }

        public async Task<DownloadResult> DownloadModelAsync(string modelName)
        {
            try
            {
                var request = new RestRequest("/models/download", Method.Post);
                request.AddJsonBody(new { model_name = modelName });
                var response = await _client.PostAsync<DownloadResponse>(request);
                
                if (response != null)
                {
                    return new DownloadResult 
                    { 
                        Success = response.Success, 
                        ErrorMessage = response.ErrorMessage 
                    };
                }
                return new DownloadResult { Success = false, ErrorMessage = "下载失败" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"下载模型失败: {ex.Message}");
                return new DownloadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<CacheInfo?> GetModelCacheInfoAsync()
        {
            try
            {
                var request = new RestRequest("/models/cache");
                var response = await _client.GetAsync<CacheInfo>(request);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取模型缓存信息失败: {ex.Message}");
                return null;
            }
        }

        public async Task<LoadResult> LoadModelAsync(string modelName)
        {
            try
            {
                var request = new RestRequest($"/models/{modelName}/load", Method.Post);
                var response = await _client.ExecuteAsync(request);
                
                if (response.IsSuccessful && response.Content != null)
                {
                    var result = Newtonsoft.Json.JsonConvert.DeserializeObject<LoadResult>(response.Content);
                    return result ?? new LoadResult { Success = false, ErrorMessage = "加载失败" };
                }
                return new LoadResult { Success = false, ErrorMessage = "服务器无响应" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载模型失败: {ex.Message}");
                return new LoadResult { Success = false, ErrorMessage = ex.Message };
            }
        }
        
        // ==================== 响应类型 ====================

        private class ImageListResponse
        {
            public List<string> Images { get; set; } = new List<string>();
        }

        private class ImageResponse
        {
            public string Image { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        private class DetectResponse
        {
            public List<BoundingBox> Detections { get; set; } = new List<BoundingBox>();
        }

        private class ModelClassesResponse
        {
            public List<string> Classes { get; set; } = new List<string>();
        }

        private class ModelsResponse
        {
            public List<ModelInfo> Models { get; set; } = new List<ModelInfo>();
        }
        
        private class ChatRequestInternal
        {
            public List<ChatMessageInternal> Messages { get; set; } = new List<ChatMessageInternal>();
            public string? ImageName { get; set; }
        }

        private class ChatMessageInternal
        {
            public string Role { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        private class ChatResponseInternal
        {
            public string Message { get; set; } = string.Empty;
            public string Role { get; set; } = "assistant";
        }

        private class DownloadResponse
        {
            public bool Success { get; set; }
            public int DownloadedCount { get; set; }
            public int FailedCount { get; set; }
            public string? ErrorMessage { get; set; }
        }

        private class CudaCheckResponse
        {
            [JsonProperty("available")]
            public bool Available { get; set; }

            [JsonProperty("pytorch_version")]
            public string? PyTorchVersion { get; set; }

            [JsonProperty("cuda_version")]
            public string? CudaVersion { get; set; }

            [JsonProperty("device_name")]
            public string? DeviceName { get; set; }

            [JsonProperty("device_count")]
            public int DeviceCount { get; set; }

            [JsonProperty("gpu_memory_gb")]
            public double? GpuMemoryGb { get; set; }

            [JsonProperty("driver_version")]
            public string? DriverVersion { get; set; }

            [JsonProperty("compute_capability")]
            public string? ComputeCapability { get; set; }

            [JsonProperty("system")]
            public string? System { get; set; }

            [JsonProperty("python_version")]
            public string? PythonVersion { get; set; }

            [JsonProperty("reason")]
            public string? Reason { get; set; }
        }
    }

    public class CudaInfo
    {
        public bool Available { get; set; }
        public string? PyTorchVersion { get; set; }
        public string? CudaVersion { get; set; }
        public string? DeviceName { get; set; }
        public int DeviceCount { get; set; }
        public double? GpuMemoryGb { get; set; }
        public string? DriverVersion { get; set; }
        public string? ComputeCapability { get; set; }
        public string? System { get; set; }
        public string? PythonVersion { get; set; }
        public string? Reason { get; set; }
    }

    public class DownloadResult
    {
        public bool Success { get; set; }
        public int DownloadedCount { get; set; }
        public int FailedCount { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Pt { get; set; }
        public string? Onnx { get; set; }
    }

    // ==================== 响应类型 ====================

    class AvailableModelsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("models")]
        public List<string>? Models { get; set; }
    }

    // ==================== 高级检测响应类型 ====================

    public class PoseDetectResponse
    {
        [JsonProperty("poses")]
        public List<PoseData>? Poses { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class PoseData
    {
        [JsonProperty("keypoints")]
        public List<List<double>>? Keypoints { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }
    }

    public class PoseDetectResult
    {
        public List<PoseData>? Poses { get; set; }
        public int Count { get; set; }
    }

    public class SegmentDetectResponse
    {
        [JsonProperty("segments")]
        public List<SegmentData>? Segments { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class SegmentData
    {
        [JsonProperty("class_id")]
        public int ClassId { get; set; }

        [JsonProperty("class_name")]
        public string ClassName { get; set; } = string.Empty;

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("bbox")]
        public BBox? Bbox { get; set; }

        [JsonProperty("segmentation")]
        public List<List<double>>? Segmentation { get; set; }
    }

    public class BBox
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }
    }

    public class SegmentDetectResult
    {
        public List<SegmentData>? Segments { get; set; }
        public int Count { get; set; }
    }

    public class LicensePlateResponse
    {
        [JsonProperty("plates")]
        public List<LicensePlateData>? Plates { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class LicensePlateData
    {
        [JsonProperty("vehicle")]
        public VehicleData? Vehicle { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }
    }

    public class VehicleData
    {
        [JsonProperty("class_id")]
        public int ClassId { get; set; }

        [JsonProperty("class_name")]
        public string ClassName { get; set; } = string.Empty;

        [JsonProperty("bbox")]
        public BBox? Bbox { get; set; }
    }

    public class LicensePlateResult
    {
        public List<LicensePlateData>? Plates { get; set; }
        public int Count { get; set; }
    }

    // ==================== OpenCV 更多功能响应类型 ====================

    public class FaceDetectResponse
    {
        [JsonProperty("faces")]
        public List<FaceData>? Faces { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class FaceData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("eyes")]
        public List<EyeData>? Eyes { get; set; }
    }

    public class EyeData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }
    }

    public class FaceDetectResult
    {
        public List<FaceData>? Faces { get; set; }
        public int Count { get; set; }
    }

    public class LaneDetectResponse
    {
        [JsonProperty("lane_lines")]
        public List<LaneLineData>? LaneLines { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class LaneDetectResult
    {
        public List<LaneLineData>? LaneLines { get; set; }
        public int Count { get; set; }
    }

    public class LaneLineData
    {
        [JsonProperty("x1")]
        public double X1 { get; set; }

        [JsonProperty("y1")]
        public double Y1 { get; set; }

        [JsonProperty("x2")]
        public double X2 { get; set; }

        [JsonProperty("y2")]
        public double Y2 { get; set; }
    }

    public class HandDetectResponse
    {
        [JsonProperty("hands")]
        public List<HandData>? Hands { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class HandDetectResult
    {
        public List<HandData>? Hands { get; set; }
        public int Count { get; set; }
    }

    public class HandData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }

        [JsonProperty("area")]
        public double Area { get; set; }

        [JsonProperty("fingers")]
        public int Fingers { get; set; }
    }

    public class BoundingBoxData
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("area")]
        public double Area { get; set; }
    }

    // ==================== 摄像头响应类型 ====================

    public class CameraInitResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }

    public class CameraInitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class CameraFrameResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonProperty("image")]
        public string? Image { get; set; }

        [JsonProperty("faces")]
        public List<BoundingBoxData>? Faces { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("lane_lines")]
        public List<LaneLineData>? LaneLines { get; set; }

        [JsonProperty("hands")]
        public List<HandData>? Hands { get; set; }
    }

    public class CameraFrameResult
    {
        public bool Success { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string? Image { get; set; }
        public List<BoundingBoxData>? Faces { get; set; }
        public int FaceCount { get; set; }
        public List<LaneLineData>? LaneLines { get; set; }
        public int LaneCount { get; set; }
        public List<HandData>? Hands { get; set; }
        public int HandCount { get; set; }
    }

    public class CameraStopResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }

    // ==================== 模型管理相关 ====================

    public class ModelConfigInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("task_type")]
        public string TaskType { get; set; } = string.Empty;

        [JsonProperty("model_path")]
        public string ModelPath { get; set; } = string.Empty;

        [JsonProperty("config")]
        public Dictionary<string, object>? Config { get; set; }

        [JsonProperty("is_loaded")]
        public bool IsLoaded { get; set; }
    }

    public class ModelConfigsResponse
    {
        [JsonProperty("configs")]
        public List<ModelConfigInfo> Configs { get; set; } = new List<ModelConfigInfo>();
    }

    public class CacheInfo
    {
        [JsonProperty("total_models")]
        public int TotalModels { get; set; }

        [JsonProperty("cached_models")]
        public int CachedModels { get; set; }

        [JsonProperty("total_size_mb")]
        public double TotalSizeMb { get; set; }

        [JsonProperty("models")]
        public List<ModelCacheInfo>? Models { get; set; }
    }

    public class ModelCacheInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("cached")]
        public bool Cached { get; set; }

        [JsonProperty("path")]
        public string? Path { get; set; }

        [JsonProperty("size_mb")]
        public double SizeMb { get; set; }

        [JsonProperty("downloaded_at")]
        public string? DownloadedAt { get; set; }
    }

    public class LoadResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("error")]
        public string? ErrorMessage { get; set; }
    }
}
