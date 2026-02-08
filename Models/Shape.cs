using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YoloAnnotator.Models
{
    /// <summary>
    /// 标注形状类型
    /// </summary>
    public enum ShapeType
    {
        Rectangle,      // 矩形
        Rotation,       // 旋转框
        Polygon,        // 多边形
        Circle,         // 圆形
        Line,           // 线段
        Point,          // 点
        Keypoints       // 关键点（姿态估计）
    }

    /// <summary>
    /// 标注形状基类
    /// </summary>
    public class Shape
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("shape_type")]
        public string ShapeType { get; set; } = "rectangle";

        [JsonPropertyName("points")]
        public List<List<float>> Points { get; set; } = new List<List<float>>();

        [JsonPropertyName("group_id")]
        public int? GroupId { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("difficult")]
        public bool Difficult { get; set; } = false;

        [JsonPropertyName("flags")]
        public Dictionary<string, bool>? Flags { get; set; }

        [JsonPropertyName("attributes")]
        public Dictionary<string, object>? Attributes { get; set; }

        [JsonPropertyName("score")]
        public float? Score { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        /// <summary>
        /// 创建矩形标注
        /// </summary>
        public static Shape CreateRectangle(float x1, float y1, float x2, float y2, string label)
        {
            return new Shape
            {
                Label = label,
                ShapeType = "rectangle",
                Points = new List<List<float>>
                {
                    new List<float> { x1, y1 },
                    new List<float> { x2, y2 }
                }
            };
        }

        /// <summary>
        /// 创建旋转框标注
        /// </summary>
        public static Shape CreateRotation(float cx, float cy, float width, float height, float angle, string label)
        {
            // 计算旋转框的四个顶点
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            var halfW = width / 2;
            var halfH = height / 2;

            var points = new List<List<float>>
            {
                new List<float> { cx + (-halfW * cos - (-halfH) * sin), cy + (-halfW * sin + (-halfH) * cos) },
                new List<float> { cx + (halfW * cos - (-halfH) * sin), cy + (halfW * sin + (-halfH) * cos) },
                new List<float> { cx + (halfW * cos - halfH * sin), cy + (halfW * sin + halfH * cos) },
                new List<float> { cx + (-halfW * cos - halfH * sin), cy + (-halfW * sin + halfH * cos) }
            };

            return new Shape
            {
                Label = label,
                ShapeType = "rotation",
                Points = points
            };
        }

        /// <summary>
        /// 创建多边形标注
        /// </summary>
        public static Shape CreatePolygon(List<List<float>> points, string label)
        {
            return new Shape
            {
                Label = label,
                ShapeType = "polygon",
                Points = points
            };
        }

        /// <summary>
        /// 创建圆形标注
        /// </summary>
        public static Shape CreateCircle(float cx, float cy, float radius, string label, int segments = 32)
        {
            var points = new List<List<float>>();
            for (int i = 0; i < segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                var x = cx + radius * (float)Math.Cos(angle);
                var y = cy + radius * (float)Math.Sin(angle);
                points.Add(new List<float> { x, y });
            }

            return new Shape
            {
                Label = label,
                ShapeType = "circle",
                Points = points
            };
        }

        /// <summary>
        /// 创建线段标注
        /// </summary>
        public static Shape CreateLine(float x1, float y1, float x2, float y2, string label)
        {
            return new Shape
            {
                Label = label,
                ShapeType = "line",
                Points = new List<List<float>>
                {
                    new List<float> { x1, y1 },
                    new List<float> { x2, y2 }
                }
            };
        }

        /// <summary>
        /// 创建点标注
        /// </summary>
        public static Shape CreatePoint(float x, float y, string label)
        {
            return new Shape
            {
                Label = label,
                ShapeType = "point",
                Points = new List<List<float>>
                {
                    new List<float> { x, y }
                }
            };
        }

        /// <summary>
        /// 创建关键点标注（姿态估计）
        /// </summary>
        public static Shape CreateKeypoints(List<List<float>> keypoints, string label)
        {
            return new Shape
            {
                Label = label,
                ShapeType = "keypoints",
                Points = keypoints
            };
        }
    }
}
