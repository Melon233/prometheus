using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>第一版 Shadertoy GLSL 导入器：将常见 mainImage 片元代码生成为 URP Shader 和 Material。</summary>
public sealed class ShadertoyGlslImporterWindow : EditorWindow
{
    private const string MenuPath = "Prometheus/Shader/Shadertoy GLSL Importer";
    private const string DefaultOutputFolder = "Assets/EditorRes/ShadertoyGenerated";
    private string shaderName = "ShadertoyGenerated";
    private string outputFolder = DefaultOutputFolder;
    private string source = "void mainImage(out vec4 fragColor, in vec2 fragCoord)\\n{\\n    fragColor = vec4(fragCoord.xy / iResolution.xy, 0.0, 1.0);\\n}";
    private Vector2 scrollPosition;

    [MenuItem(MenuPath)]
    private static void OpenWindow() { GetWindow<ShadertoyGlslImporterWindow>("Shadertoy Importer").Show(); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Shadertoy GLSL -> Unity URP Shader", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("支持标准 mainImage(out vec4 fragColor, in vec2 fragCoord) 入口和常见 iResolution/iTime 内置变量。Buffer、多 Pass、iChannel 反馈暂不支持。", MessageType.Info);
        shaderName = EditorGUILayout.TextField("Shader 名称", shaderName);
        using (new EditorGUILayout.HorizontalScope()) { outputFolder = EditorGUILayout.TextField("输出目录", outputFolder); if (GUILayout.Button("选择", GUILayout.Width(60))) { string absolute = EditorUtility.OpenFolderPanel("选择输出目录", Application.dataPath, ""); if (!string.IsNullOrEmpty(absolute) && absolute.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) outputFolder = "Assets" + absolute.Substring(Application.dataPath.Length).Replace('\\', '/'); } }
        EditorGUILayout.LabelField("GLSL 源码", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(260));
        source = EditorGUILayout.TextArea(source, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("清空")) source = string.Empty; GUILayout.FlexibleSpace(); if (GUILayout.Button("生成 Shader 和 Material", GUILayout.Height(28))) Generate(); }
    }

    private void Generate()
    {
        if (string.IsNullOrWhiteSpace(shaderName) || !Regex.IsMatch(shaderName, "^[A-Za-z_][A-Za-z0-9_]*$")) { EditorUtility.DisplayDialog("无法生成", "Shader 名称只能包含字母、数字和下划线，且不能以数字开头。", "确定"); return; }
        if (!Regex.IsMatch(source, @"void\s+mainImage\s*\(\s*out\s+vec4\s+\w+\s*,\s*in\s+vec2\s+\w+\s*\)")) { EditorUtility.DisplayDialog("无法生成", "源码中未找到标准签名的 mainImage(out vec4, in vec2) 函数。", "确定"); return; }
        if (Regex.IsMatch(source, "\\b(Buffer|iChannel[0-3]|iChannelResolution|iChannelTime|mainVR|mainCubemap)\\b")) { EditorUtility.DisplayDialog("暂不支持", "第一版不支持 Buffer、iChannel（含 iChannelResolution/iChannelTime）、VR 或 Cubemap 入口，请先移除这些内容。", "确定"); return; }
        string folder = outputFolder.Replace('\\', '/').TrimEnd('/');
        if (!folder.StartsWith("Assets", StringComparison.Ordinal) || folder.Contains("..")) { EditorUtility.DisplayDialog("无法生成", "输出目录必须位于 Assets 目录内。", "确定"); return; }
        EnsureFolder(folder);
        string shaderPath = folder + "/" + shaderName + ".shader";
        string materialPath = folder + "/" + shaderName + ".mat";
        string shaderText = BuildShader(shaderName, Translate(source));
        File.WriteAllText(ToAbsolutePath(shaderPath), shaderText, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null) { EditorUtility.DisplayDialog("生成失败", "Shader 已写入，但 Unity 编译失败，请查看 Console。", "确定"); return; }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader) { name = shaderName }; AssetDatabase.CreateAsset(material, materialPath); } else { material.shader = shader; EditorUtility.SetDirty(material); }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = material;
        EditorUtility.DisplayDialog("生成完成", "已生成 Shader 和 Material：\\n" + shaderPath + "\\n" + materialPath, "确定");
    }

    // GLSL constructors accept scalar-broadcast, copy, truncating and concatenating forms that
    // HLSL constructors reject outright (HLSL requires the argument count to match exactly).
    // Expanding single-argument float2/3/4(...) calls via regex on the literal/identifier text
    // can't tell a scalar apart from an already-sized vector, so instead we defer that decision
    // to HLSL overload resolution through these helpers.
    private const string HelperFunctions =
@"float glsl_mod(float x, float y) { return x - y * floor(x / y); }
float2 glsl_mod(float2 x, float2 y) { return x - y * floor(x / y); }
float3 glsl_mod(float3 x, float3 y) { return x - y * floor(x / y); }
float4 glsl_mod(float4 x, float4 y) { return x - y * floor(x / y); }
float3 glsl_expand3(float x) { return float3(x, x, x); }
float3 glsl_expand3(float3 x) { return x; }
float3 glsl_expand3(float4 x) { return x.xyz; }
float2 glsl_expand2(float x) { return float2(x, x); }
float2 glsl_expand2(float2 x) { return x; }
float4 glsl_expand4(float x) { return float4(x, x, x, x); }
float4 glsl_expand4(float3 x, float w) { return float4(x, w); }
";

    private static string Translate(string glsl)
    {
        Match signature = Regex.Match(glsl, @"void\s+mainImage\s*\(\s*out\s+vec4\s+(\w+)\s*,\s*in\s+vec2\s+(\w+)\s*\)");
        string translated = glsl.Substring(0, signature.Index)
            + "void MainImage(out float4 " + signature.Groups[1].Value + ", in float2 " + signature.Groups[2].Value + ")"
            + glsl.Substring(signature.Index + signature.Length);

        // Strip GLSL ES precision qualifiers; HLSL has no equivalent syntax.
        translated = Regex.Replace(translated, @"precision\s+(highp|mediump|lowp)\s+\w+\s*;", string.Empty);
        translated = Regex.Replace(translated, @"\b(highp|mediump|lowp)\s+", string.Empty);

        translated = Regex.Replace(translated, "\\bvec([234])\\b", "float$1");
        translated = Regex.Replace(translated, "\\bmat([234])\\b", "float$1x$1");

        // Single-argument float2/3/4(...) constructors route through the overloaded helpers
        // above instead of naive text expansion, so scalars, copies and truncations all work.
        translated = ReplaceSingleArgConstructors(translated, "float2", "glsl_expand2");
        translated = ReplaceSingleArgConstructors(translated, "float3", "glsl_expand3");
        translated = ReplaceSingleArgConstructors(translated, "float4", "glsl_expand4");

        // GLSL's two-argument atan(y, x) is HLSL's atan2(y, x); HLSL's atan only takes one arg.
        translated = ReplaceTwoArgCallName(translated, "atan", "atan2");

        translated = translated.Replace("mix(", "lerp(").Replace("fract(", "frac(").Replace("mod(", "glsl_mod(").Replace("texture(", "SAMPLE_TEXTURE2D(");
        translated = Regex.Replace(translated, "\\biResolution\\b", "float3(_ScreenParams.xy, 1.0)");
        translated = Regex.Replace(translated, "\\biTime\\b", "_Time.y");
        translated = Regex.Replace(translated, "\\biTimeDelta\\b", "unity_DeltaTime.x");
        translated = Regex.Replace(translated, "\\biFrame\\b", "0");
        translated = Regex.Replace(translated, "\\biDate\\b", "float4(2024, 1, 1, 0)");
        translated = Regex.Replace(translated, "\\biSampleRate\\b", "44100.0");
        translated = Regex.Replace(translated, "\\biMouse\\b", "float4(0, 0, 0, 0)");

        return HelperFunctions + translated;
    }

    // Finds calls like name(arg) where the parenthesised content has no top-level comma
    // (i.e. exactly one argument, possibly containing its own nested calls), and rewrites
    // the call to replacementName(arg). Calls with 2+ top-level arguments are left untouched.
    private static string ReplaceSingleArgConstructors(string text, string name, string replacementName)
    {
        StringBuilder result = new StringBuilder();
        int cursor = 0;
        string marker = name + "(";
        int searchStart = 0;
        while (true)
        {
            int index = text.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (index < 0) { result.Append(text, cursor, text.Length - cursor); break; }
            bool isWordBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]) && text[index - 1] != '_';
            if (!isWordBoundary) { searchStart = index + marker.Length; continue; }
            int argsStart = index + marker.Length;
            int argsEnd = FindMatchingParen(text, argsStart - 1);
            if (argsEnd < 0) { searchStart = index + marker.Length; continue; }
            string args = text.Substring(argsStart, argsEnd - argsStart);
            if (!HasTopLevelComma(args))
            {
                result.Append(text, cursor, index - cursor);
                result.Append(replacementName).Append('(').Append(args).Append(')');
                cursor = argsEnd + 1;
            }
            searchStart = argsEnd + 1;
        }
        return result.ToString();
    }

    // Finds two-top-level-argument calls to `name` and renames the call to `replacementName`,
    // leaving one-argument calls to the same name untouched.
    private static string ReplaceTwoArgCallName(string text, string name, string replacementName)
    {
        StringBuilder result = new StringBuilder();
        int cursor = 0;
        string marker = name + "(";
        int searchStart = 0;
        while (true)
        {
            int index = text.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (index < 0) { result.Append(text, cursor, text.Length - cursor); break; }
            bool isWordBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]) && text[index - 1] != '_';
            if (!isWordBoundary) { searchStart = index + marker.Length; continue; }
            int argsStart = index + marker.Length;
            int argsEnd = FindMatchingParen(text, argsStart - 1);
            if (argsEnd < 0) { searchStart = index + marker.Length; continue; }
            string args = text.Substring(argsStart, argsEnd - argsStart);
            if (HasTopLevelComma(args) && CountTopLevelArgs(args) == 2)
            {
                result.Append(text, cursor, index - cursor);
                result.Append(replacementName).Append('(').Append(args).Append(')');
                cursor = argsEnd + 1;
            }
            searchStart = argsEnd + 1;
        }
        return result.ToString();
    }

    private static int FindMatchingParen(string text, int openParenIndex)
    {
        int depth = 0;
        for (int index = openParenIndex; index < text.Length; index++)
        {
            if (text[index] == '(') depth++;
            else if (text[index] == ')') { depth--; if (depth == 0) return index; }
        }
        return -1;
    }

    private static bool HasTopLevelComma(string args)
    {
        int depth = 0;
        foreach (char c in args)
        {
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0) return true;
        }
        return false;
    }

    private static int CountTopLevelArgs(string args)
    {
        int depth = 0;
        int count = 1;
        foreach (char c in args)
        {
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    private static string BuildShader(string name, string body)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Shader \"Prometheus/Shadertoy/" + name + "\"");
        builder.AppendLine("{");
        builder.AppendLine("    SubShader");
        builder.AppendLine("    {");
        builder.AppendLine("        Tags { \"RenderPipeline\"=\"UniversalPipeline\" \"RenderType\"=\"Opaque\" }");
        builder.AppendLine("        Pass");
        builder.AppendLine("        {");
        builder.AppendLine("            HLSLPROGRAM");
        builder.AppendLine("            #pragma vertex Vert");
        builder.AppendLine("            #pragma fragment Frag");
        builder.AppendLine("            #include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"");
        builder.AppendLine("            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };");
        builder.AppendLine("            struct Varyings { float4 positionHCS : SV_POSITION; };");
        builder.AppendLine("            Varyings Vert(Attributes input) { Varyings output; output.positionHCS = TransformObjectToHClip(input.positionOS.xyz); return output; }");
        builder.AppendLine("            " + body.Replace("\n", "\n            "));
        builder.AppendLine("            half4 Frag(Varyings input) : SV_Target { float4 color; MainImage(color, input.positionHCS.xy); return color; }");
        builder.AppendLine("            ENDHLSL");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++) { string next = current + "/" + parts[index]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]); current = next; }
    }

    private static string ToAbsolutePath(string assetPath) { return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath)); }
}
