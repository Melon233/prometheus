using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>从 FMOD Unity 编辑器缓存生成稳定音频事件枚举和 GUID 运行时映射。</summary>
    public static class FmodAudioEventGenerator
    {
        private const string GeneratedAssetPath = "Assets/Prometheus/Gameplay/Audio/FmodAudioEvent.Generated.cs";
        private const string EventPathPrefix = "event:/";

        private static readonly HashSet<string> ReservedIdentifiers = new HashSet<string>(StringComparer.Ordinal) { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while" };

        /// <summary>刷新 FMOD Bank 缓存并重新生成事件枚举；音频 Bank Source 必须已在 FMOD Settings 中配置。</summary>
        [MenuItem("Prometheus/Audio/Refresh Banks And Regenerate FMOD Event Enum")]
        public static void RefreshBanksAndGenerate()
        {
            EventManager.RefreshBanks();
            GenerateFromCurrentCache();
        }

        /// <summary>读取当前缓存中的全部普通 FMOD 事件并写入确定性的 C# 源文件。</summary>
        private static void GenerateFromCurrentCache()
        {
            List<EditorEventRef> cachedEvents = EventManager.Events == null ? new List<EditorEventRef>() : EventManager.Events.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path) && item.Path.StartsWith(EventPathPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
            List<GeneratedEvent> generatedEvents = CreateGeneratedEvents(cachedEvents);
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, GeneratedAssetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(absolutePath, BuildSource(generatedEvents), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(GeneratedAssetPath, ImportAssetOptions.ForceUpdate);
            if (generatedEvents.Count == 0) Debug.LogWarning("FMOD 事件枚举已生成，但当前 Bank 缓存没有 event:/ 事件。请在 FMOD Settings 配置 Bank Source，并在 FMOD Studio 构建 Banks 后重试。");
            else Debug.Log($"FMOD 事件枚举生成完成：{generatedEvents.Count} 个事件已写入 {GeneratedAssetPath}。");
        }

        /// <summary>为每个 FMOD 事件创建合法且唯一的枚举名称与稳定整数值。</summary>
        private static List<GeneratedEvent> CreateGeneratedEvents(IReadOnlyList<EditorEventRef> cachedEvents)
        {
            List<GeneratedEvent> generatedEvents = new List<GeneratedEvent>(cachedEvents.Count);
            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> usedValues = new HashSet<int> { 0 };
            for (int index = 0; index < cachedEvents.Count; index++)
            {
                EditorEventRef cachedEvent = cachedEvents[index];
                int value = CreateStableValue(cachedEvent.Path);
                while (!usedValues.Add(value)) value = value == int.MaxValue ? 1 : value + 1;
                string baseName = CreateIdentifier(cachedEvent.Path);
                string uniqueName = baseName;
                if (!usedNames.Add(uniqueName))
                {
                    uniqueName = $"{baseName}_{(uint)value:X8}";
                    usedNames.Add(uniqueName);
                }
                generatedEvents.Add(new GeneratedEvent(uniqueName, value, cachedEvent.Path, cachedEvent.Guid));
            }
            return generatedEvents;
        }

        /// <summary>使用事件路径的 FNV-1a 哈希生成稳定正整数，使枚举值不受 Bank 排序变化影响。</summary>
        private static int CreateStableValue(string eventPath)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(eventPath);
            uint hash = 2166136261u;
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= 16777619u;
            }
            int value = (int)(hash & 0x7FFFFFFF);
            return value == 0 ? 1 : value;
        }

        /// <summary>把 event:/ 路径转换为合法 C# 枚举成员名，并处理数字开头与保留关键字。</summary>
        private static string CreateIdentifier(string eventPath)
        {
            string relativePath = eventPath.StartsWith(EventPathPrefix, StringComparison.OrdinalIgnoreCase) ? eventPath.Substring(EventPathPrefix.Length) : eventPath;
            StringBuilder builder = new StringBuilder(relativePath.Length + 8);
            bool capitalizeNext = true;
            for (int index = 0; index < relativePath.Length; index++)
            {
                char character = relativePath[index];
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    capitalizeNext = true;
                    continue;
                }
                if (capitalizeNext && char.IsLetter(character)) character = char.ToUpperInvariant(character);
                builder.Append(character);
                capitalizeNext = false;
            }
            if (builder.Length == 0) builder.Append("Event");
            if (!char.IsLetter(builder[0]) && builder[0] != '_') builder.Insert(0, "Event_");
            string identifier = builder.ToString();
            return ReservedIdentifiers.Contains(identifier) ? $"Event_{identifier}" : identifier;
        }

        /// <summary>生成包含完整注释、枚举、GUID 映射和编辑器路径映射的 C# 源文件。</summary>
        private static string BuildSource(IReadOnlyList<GeneratedEvent> generatedEvents)
        {
            StringBuilder source = new StringBuilder(4096 + generatedEvents.Count * 240);
            source.AppendLine("// <auto-generated>");
            source.AppendLine("// 此文件由 Tools/Prometheus/Audio/Refresh Banks And Regenerate FMOD Event Enum 生成；请勿手工修改。");
            source.AppendLine($"// 生成时共发现 {generatedEvents.Count} 个 event:/ 音频事件，枚举值来自稳定路径哈希，运行时通过 GUID 播放。");
            source.AppendLine("// </auto-generated>");
            source.AppendLine("using FMOD;");
            source.AppendLine();
            source.AppendLine("namespace Xuan.Prometheus");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>定义从当前 FMOD Bank 缓存生成的稳定音频事件标识。</summary>");
            source.AppendLine("    public enum FmodAudioEvent");
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>表示不绑定任何 FMOD 音频事件。</summary>");
            source.AppendLine("        None = 0,");
            for (int index = 0; index < generatedEvents.Count; index++)
            {
                GeneratedEvent generatedEvent = generatedEvents[index];
                source.AppendLine($"        /// <summary>FMOD 事件 {EscapeXml(generatedEvent.Path)}。</summary>");
                source.AppendLine($"        {generatedEvent.Name} = {generatedEvent.Value},");
            }
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine("    /// <summary>保存生成的音频事件标识到 FMOD GUID 的运行时映射。</summary>");
            source.AppendLine("    internal static class FmodAudioEventCatalog");
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>尝试解析音频事件对应的稳定 FMOD GUID。</summary>");
            source.AppendLine("        public static bool TryGetGuid(FmodAudioEvent audioEvent, out GUID guid)");
            source.AppendLine("        {");
            source.AppendLine("            switch (audioEvent)");
            source.AppendLine("            {");
            for (int index = 0; index < generatedEvents.Count; index++) AppendGuidCase(source, generatedEvents[index]);
            source.AppendLine("                default:");
            source.AppendLine("                    guid = default;");
            source.AppendLine("                    return false;");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("#if UNITY_EDITOR");
            source.AppendLine("        /// <summary>尝试解析编辑器中用于诊断和展示的 FMOD 事件路径。</summary>");
            source.AppendLine("        public static bool TryGetPath(FmodAudioEvent audioEvent, out string path)");
            source.AppendLine("        {");
            source.AppendLine("            switch (audioEvent)");
            source.AppendLine("            {");
            for (int index = 0; index < generatedEvents.Count; index++) AppendPathCase(source, generatedEvents[index]);
            source.AppendLine("                default:");
            source.AppendLine("                    path = string.Empty;");
            source.AppendLine("                    return false;");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine("#endif");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        /// <summary>向生成文件加入一个枚举到 FMOD GUID 的 switch 分支。</summary>
        private static void AppendGuidCase(StringBuilder source, GeneratedEvent generatedEvent)
        {
            source.AppendLine($"                case FmodAudioEvent.{generatedEvent.Name}:");
            source.AppendLine($"                    guid = new GUID {{ Data1 = {generatedEvent.Guid.Data1}, Data2 = {generatedEvent.Guid.Data2}, Data3 = {generatedEvent.Guid.Data3}, Data4 = {generatedEvent.Guid.Data4} }};");
            source.AppendLine("                    return true;");
        }

        /// <summary>向生成文件加入一个枚举到编辑器事件路径的 switch 分支。</summary>
        private static void AppendPathCase(StringBuilder source, GeneratedEvent generatedEvent)
        {
            source.AppendLine($"                case FmodAudioEvent.{generatedEvent.Name}:");
            source.AppendLine($"                    path = \"{EscapeString(generatedEvent.Path)}\";");
            source.AppendLine("                    return true;");
        }

        /// <summary>转义生成代码中的 C# 字符串内容。</summary>
        private static string EscapeString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>转义生成注释中的 XML 特殊字符。</summary>
        private static string EscapeXml(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>保存单个 FMOD 缓存事件转换后的代码生成数据。</summary>
        private sealed class GeneratedEvent
        {
            /// <summary>创建一个确定性的生成事件描述。</summary>
            public GeneratedEvent(string name, int value, string path, FMOD.GUID guid)
            {
                Name = name;
                Value = value;
                Path = path;
                Guid = guid;
            }

            /// <summary>获取合法且唯一的枚举成员名。</summary>
            public string Name { get; }

            /// <summary>获取由事件路径稳定生成的枚举整数值。</summary>
            public int Value { get; }

            /// <summary>获取 FMOD Studio 事件路径。</summary>
            public string Path { get; }

            /// <summary>获取 FMOD Bank 缓存提供的事件 GUID。</summary>
            public FMOD.GUID Guid { get; }
        }
    }
}
