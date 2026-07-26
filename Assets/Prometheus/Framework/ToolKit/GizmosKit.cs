using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 集中管理运行时调试图形，并通过 Unity Gizmos 在 Scene/Game 视图中绘制。
    /// 调用方无需实现 OnDrawGizmos；duration 为 0 时图形保留一帧。
    /// </summary>
    public class GizmosKit : MonoSingleton<GizmosKit>
    {
        private const int DefaultSegments = 32;
        private const float DirectionEpsilon = 0.0001f;

        private readonly List<DrawCommand> commands = new();

        private enum ShapeType
        {
            Lines,
            Sphere,
            WireSphere,
            Cube,
            WireCube,
            Frustum
        }

        private sealed class DrawCommand
        {
            public ShapeType Shape;
            public Color Color;
            public Vector3[] Points;
            public Vector3 Center;
            public Vector3 Size;
            public Quaternion Rotation = Quaternion.identity;
            public float Radius;
            public float FieldOfView;
            public float MinRange;
            public float MaxRange;
            public float Aspect;
            public int LastFrame;
            public float ExpireTime;
            public bool HasDuration;

            public bool IsAlive(int frame, float time)
            {
                return HasDuration ? time <= ExpireTime : frame <= LastFrame;
            }
        }

        /// <summary>
        /// 绘制一条线段。
        /// </summary>
        public void DrawLine(
            Vector3 start,
            Vector3 end,
            Color? color = null,
            float duration = 0f)
        {
            AddLines(new[] { start, end }, color, duration);
        }

        /// <summary>
        /// 从 origin 开始，按照 direction 的长度和方向绘制射线。
        /// </summary>
        public void DrawRay(
            Vector3 origin,
            Vector3 direction,
            Color? color = null,
            float duration = 0f)
        {
            DrawLine(origin, origin + direction, color, duration);
        }

        /// <summary>
        /// 按顺序连接顶点；closed 为 true 时额外连接首尾顶点。
        /// </summary>
        public void DrawPolyline(
            IReadOnlyList<Vector3> points,
            bool closed = false,
            Color? color = null,
            float duration = 0f)
        {
            if (points == null || points.Count < 2)
                return;

            int segmentCount = closed ? points.Count : points.Count - 1;
            var linePoints = new Vector3[segmentCount * 2];
            for (int i = 0; i < segmentCount; i++)
            {
                linePoints[i * 2] = points[i];
                linePoints[i * 2 + 1] = points[(i + 1) % points.Count];
            }

            AddLines(linePoints, color, duration);
        }

        /// <summary>
        /// 绘制任意朝向的线框圆。
        /// </summary>
        public void DrawWireCircle(
            Vector3 center,
            Vector3 normal,
            float radius,
            Color? color = null,
            float duration = 0f,
            int segments = DefaultSegments)
        {
            if (!TryCreateBasis(normal, out Vector3 tangent, out Vector3 bitangent))
                return;

            radius = Mathf.Abs(radius);
            if (radius <= 0f)
                return;

            segments = Mathf.Max(3, segments);
            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                points[i] = center +
                            (tangent * Mathf.Cos(angle) +
                             bitangent * Mathf.Sin(angle)) * radius;
            }

            DrawPolyline(points, true, color, duration);
        }

        /// <summary>
        /// 从 fromDirection 开始，绕 normal 绘制指定角度的线框圆弧。
        /// </summary>
        public void DrawWireArc(
            Vector3 center,
            Vector3 normal,
            Vector3 fromDirection,
            float angle,
            float radius,
            Color? color = null,
            float duration = 0f,
            int segments = DefaultSegments)
        {
            if (normal.sqrMagnitude <= DirectionEpsilon ||
                fromDirection.sqrMagnitude <= DirectionEpsilon)
            {
                return;
            }

            Vector3 normalizedNormal = normal.normalized;
            Vector3 startDirection =
                Vector3.ProjectOnPlane(fromDirection, normalizedNormal);
            if (startDirection.sqrMagnitude <= DirectionEpsilon)
                return;

            radius = Mathf.Abs(radius);
            if (radius <= 0f)
                return;

            int arcSegments = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Abs(angle) / 360f *
                                Mathf.Max(3, segments)));
            var points = new Vector3[arcSegments + 1];
            startDirection.Normalize();

            for (int i = 0; i <= arcSegments; i++)
            {
                float currentAngle = angle * i / arcSegments;
                points[i] = center +
                            Quaternion.AngleAxis(
                                currentAngle,
                                normalizedNormal) *
                            startDirection * radius;
            }

            DrawPolyline(points, false, color, duration);
        }

        public void DrawWireSphere(
            Vector3 center,
            float radius,
            Color? color = null,
            float duration = 0f)
        {
            AddPrimitive(
                ShapeType.WireSphere,
                center,
                Vector3.zero,
                Quaternion.identity,
                Mathf.Abs(radius),
                color,
                duration);
        }

        public void DrawSphere(
            Vector3 center,
            float radius,
            Color? color = null,
            float duration = 0f)
        {
            AddPrimitive(
                ShapeType.Sphere,
                center,
                Vector3.zero,
                Quaternion.identity,
                Mathf.Abs(radius),
                color,
                duration);
        }

        public void DrawWireCube(
            Vector3 center,
            Vector3 size,
            Quaternion rotation,
            Color? color = null,
            float duration = 0f)
        {
            AddPrimitive(
                ShapeType.WireCube,
                center,
                Abs(size),
                rotation,
                0f,
                color,
                duration);
        }

        public void DrawWireCube(
            Bounds bounds,
            Color? color = null,
            float duration = 0f)
        {
            DrawWireCube(
                bounds.center,
                bounds.size,
                Quaternion.identity,
                color,
                duration);
        }

        public void DrawCube(
            Vector3 center,
            Vector3 size,
            Quaternion rotation,
            Color? color = null,
            float duration = 0f)
        {
            AddPrimitive(
                ShapeType.Cube,
                center,
                Abs(size),
                rotation,
                0f,
                color,
                duration);
        }

        /// <summary>
        /// 绘制线框胶囊。pointA 和 pointB 表示两个半球的球心。
        /// </summary>
        public void DrawWireCapsule(
            Vector3 pointA,
            Vector3 pointB,
            float radius,
            Color? color = null,
            float duration = 0f)
        {
            radius = Mathf.Abs(radius);
            Vector3 axis = pointB - pointA;
            if (axis.sqrMagnitude <= DirectionEpsilon)
            {
                DrawWireSphere(pointA, radius, color, duration);
                return;
            }

            Vector3 normalizedAxis = axis.normalized;
            Vector3 helper =
                Mathf.Abs(Vector3.Dot(normalizedAxis, Vector3.up)) > 0.99f
                    ? Vector3.right
                    : Vector3.up;
            Vector3 right =
                Vector3.Cross(normalizedAxis, helper).normalized * radius;
            Vector3 forward =
                Vector3.Cross(normalizedAxis, right).normalized * radius;

            // 两个球体给出半球轮廓，四条母线连接胶囊主体。
            DrawWireSphere(pointA, radius, color, duration);
            DrawWireSphere(pointB, radius, color, duration);
            AddLines(
                new[]
                {
                    pointA + right, pointB + right,
                    pointA - right, pointB - right,
                    pointA + forward, pointB + forward,
                    pointA - forward, pointB - forward
                },
                color,
                duration);
        }

        /// <summary>
        /// 绘制箭头；headLength 和 headAngle 控制箭头头部尺寸。
        /// </summary>
        public void DrawArrow(
            Vector3 start,
            Vector3 end,
            Color? color = null,
            float duration = 0f,
            float headLength = 0.25f,
            float headAngle = 20f)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length <= DirectionEpsilon)
                return;

            Vector3 forward = direction / length;
            headLength = Mathf.Clamp(Mathf.Abs(headLength), 0f, length);
            float headRadius =
                Mathf.Tan(Mathf.Clamp(headAngle, 0f, 89f) * Mathf.Deg2Rad) *
                headLength;

            Vector3 helper =
                Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                    ? Vector3.right
                    : Vector3.up;
            Vector3 right = Vector3.Cross(forward, helper).normalized;
            Vector3 up = Vector3.Cross(right, forward).normalized;
            Vector3 headBase = end - forward * headLength;

            AddLines(
                new[]
                {
                    start, end,
                    end, headBase + right * headRadius,
                    end, headBase - right * headRadius,
                    end, headBase + up * headRadius,
                    end, headBase - up * headRadius
                },
                color,
                duration);
        }

        /// <summary>
        /// 按给定位置和旋转绘制相机视锥。
        /// </summary>
        public void DrawFrustum(
            Vector3 position,
            Quaternion rotation,
            float fieldOfView,
            float minRange,
            float maxRange,
            float aspect,
            Color? color = null,
            float duration = 0f)
        {
            float safeMinRange = Mathf.Max(0f, minRange);
            float safeMaxRange = Mathf.Max(safeMinRange, maxRange);
            commands.Add(CreateCommand(color, duration, new DrawCommand
            {
                Shape = ShapeType.Frustum,
                Center = position,
                Rotation = rotation,
                FieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f),
                MinRange = safeMinRange,
                MaxRange = safeMaxRange,
                Aspect = Mathf.Max(DirectionEpsilon, aspect)
            }));
        }

        /// <summary>
        /// 清除所有尚未过期的调试图形。
        /// </summary>
        public void Clear()
        {
            commands.Clear();
        }

        private void Update()
        {
            RemoveExpiredCommands();
        }

        private void OnDrawGizmos()
        {
            RemoveExpiredCommands();

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            try
            {
                foreach (DrawCommand command in commands)
                {
                    Gizmos.color = command.Color;
                    Gizmos.matrix = Matrix4x4.identity;
                    Draw(command);
                }
            }
            finally
            {
                // Gizmos 是全局状态，必须恢复，避免影响其它组件的绘制结果。
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
            }
        }

        private static void Draw(DrawCommand command)
        {
            switch (command.Shape)
            {
                case ShapeType.Lines:
                    for (int i = 0; i + 1 < command.Points.Length; i += 2)
                        Gizmos.DrawLine(command.Points[i], command.Points[i + 1]);
                    break;
                case ShapeType.Sphere:
                    Gizmos.DrawSphere(command.Center, command.Radius);
                    break;
                case ShapeType.WireSphere:
                    Gizmos.DrawWireSphere(command.Center, command.Radius);
                    break;
                case ShapeType.Cube:
                    DrawCubePrimitive(command, false);
                    break;
                case ShapeType.WireCube:
                    DrawCubePrimitive(command, true);
                    break;
                case ShapeType.Frustum:
                    Gizmos.matrix = Matrix4x4.TRS(
                        command.Center,
                        command.Rotation,
                        Vector3.one);
                    Gizmos.DrawFrustum(
                        Vector3.zero,
                        command.FieldOfView,
                        command.MaxRange,
                        command.MinRange,
                        command.Aspect);
                    break;
            }
        }

        private static void DrawCubePrimitive(
            DrawCommand command,
            bool wireframe)
        {
            Gizmos.matrix = Matrix4x4.TRS(
                command.Center,
                command.Rotation,
                Vector3.one);

            if (wireframe)
                Gizmos.DrawWireCube(Vector3.zero, command.Size);
            else
                Gizmos.DrawCube(Vector3.zero, command.Size);
        }

        private void AddLines(
            Vector3[] points,
            Color? color,
            float duration)
        {
            commands.Add(CreateCommand(color, duration, new DrawCommand
            {
                Shape = ShapeType.Lines,
                Points = points
            }));
        }

        private void AddPrimitive(
            ShapeType shape,
            Vector3 center,
            Vector3 size,
            Quaternion rotation,
            float radius,
            Color? color,
            float duration)
        {
            commands.Add(CreateCommand(color, duration, new DrawCommand
            {
                Shape = shape,
                Center = center,
                Size = size,
                Rotation = rotation,
                Radius = radius
            }));
        }

        private static DrawCommand CreateCommand(
            Color? color,
            float duration,
            DrawCommand command)
        {
            duration = Mathf.Max(0f, duration);
            command.Color = color ?? Color.white;
            command.HasDuration = duration > 0f;
            command.ExpireTime = Time.realtimeSinceStartup + duration;

            // 多保留一个 frame，避免调用顺序导致命令在 Gizmo 回调前被清理。
            command.LastFrame = Time.frameCount + 1;
            return command;
        }

        private void RemoveExpiredCommands()
        {
            int frame = Time.frameCount;
            float time = Time.realtimeSinceStartup;
            commands.RemoveAll(command => !command.IsAlive(frame, time));
        }

        private static bool TryCreateBasis(
            Vector3 normal,
            out Vector3 tangent,
            out Vector3 bitangent)
        {
            if (normal.sqrMagnitude <= DirectionEpsilon)
            {
                tangent = default;
                bitangent = default;
                return false;
            }

            normal.Normalize();
            Vector3 helper =
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f
                    ? Vector3.right
                    : Vector3.up;
            tangent = Vector3.Cross(normal, helper).normalized;
            bitangent = Vector3.Cross(normal, tangent).normalized;
            return true;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }
    }
}
