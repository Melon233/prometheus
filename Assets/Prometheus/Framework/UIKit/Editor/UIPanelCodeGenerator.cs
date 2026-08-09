using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 直接读取最终 UI Prefab 根节点的 UIComponentBinder，并生成只包含组件表的 PanelBase 与可安全保留业务改动的 Panel 脚本。
    /// </summary>
    public static class UIPanelCodeGenerator
    {
        private const string GeneratedBaseDirectory = "Assets/Prometheus/Framework/UIKit/Generated";
        private const string PanelScriptDirectory = "Assets/Prometheus/Gameplay/UI";

        /// <summary>
        /// 从 Project 视图当前选中的 UI Prefab 生成面板代码。
        /// </summary>
        [MenuItem("Tools/Prometheus/UIKit/Generate Selected Panel", false, 100)]
        public static void GenerateSelectedPanel()
        {
            GameObject selectedObject = Selection.activeObject as GameObject;
            UIComponentBinder binder = selectedObject != null ? selectedObject.GetComponent<UIComponentBinder>() : null;
            if (binder == null)
            {
                EditorUtility.DisplayDialog("UIKit Generator", "Select a UI prefab whose root contains UIComponentBinder.", "OK");
                return;
            }

            Generate(binder);
        }

        /// <summary>
        /// 校验指定 Binder 所属 Prefab，并生成强类型组件表和首次业务脚本模板。
        /// </summary>
        /// <param name="binder">位于目标 Prefab 根节点的组件绑定器。</param>
        public static void Generate(UIComponentBinder binder)
        {
            if (binder == null)
                throw new ArgumentNullException(nameof(binder));

            string prefabPath = ResolvePrefabPath(binder);
            ValidateBinder(binder, prefabPath);
            string panelName = ToTypeIdentifier(Path.GetFileNameWithoutExtension(prefabPath));
            string basePath = $"{GeneratedBaseDirectory}/{panelName}Base.g.cs";
            string panelPath = $"{PanelScriptDirectory}/{panelName}.cs";
            Directory.CreateDirectory(GeneratedBaseDirectory);
            Directory.CreateDirectory(PanelScriptDirectory);
            File.WriteAllText(basePath, BuildPanelBaseSource(panelName, binder.Bindings), new UTF8Encoding(false));

            if (!File.Exists(panelPath))
                File.WriteAllText(panelPath, BuildPanelSource(panelName, binder.Bindings), new UTF8Encoding(false));

            AssetDatabase.ImportAsset(basePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(panelPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log($"[UIKit Generator] Generated '{basePath}' and preserved or created '{panelPath}'.", binder);
        }

        /// <summary>
        /// 解析普通 Prefab 资源或 Prefab Stage 中对象对应的资产路径。
        /// </summary>
        private static string ResolvePrefabPath(UIComponentBinder binder)
        {
            string prefabPath = AssetDatabase.GetAssetPath(binder);
            if (!string.IsNullOrWhiteSpace(prefabPath))
                return prefabPath;

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (IsInsidePrefabStage(binder, prefabStage))
                return prefabStage.assetPath;

            prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(binder.gameObject);
            if (string.IsNullOrWhiteSpace(prefabPath))
                throw new InvalidOperationException("UIComponentBinder must belong to a saved prefab asset or the current Prefab Stage root.");

            return prefabPath;
        }

        /// <summary>
        /// 在写文件前验证 Binder 位置、唯一性、绑定名称、组件引用和字段标识符。
        /// </summary>
        private static void ValidateBinder(UIComponentBinder binder, string prefabPath)
        {
            GameObject prefabRoot = ResolvePrefabRoot(binder);
            if (binder.gameObject != prefabRoot)
                throw new InvalidOperationException($"UIComponentBinder on '{prefabPath}' must be attached to the prefab root.");

            if (binder.GetComponents<UIComponentBinder>().Length != 1)
                throw new InvalidOperationException($"Prefab '{prefabPath}' must contain exactly one UIComponentBinder on its root.");

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> identifiers = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < binder.Bindings.Count; index++)
            {
                UIComponentBinding binding = binder.Bindings[index] ?? throw new InvalidOperationException($"Binding {index} on '{prefabPath}' is null.");
                if (string.IsNullOrWhiteSpace(binding.Name))
                    throw new InvalidOperationException($"Binding {index} on '{prefabPath}' has an empty name.");

                if (!names.Add(binding.Name))
                    throw new InvalidOperationException($"Binding name '{binding.Name}' on '{prefabPath}' is duplicated.");

                if (binding.Component == null)
                    throw new InvalidOperationException($"Binding '{binding.Name}' on '{prefabPath}' does not reference a component.");

                if (!binding.Component.transform.IsChildOf(binder.transform))
                    throw new InvalidOperationException($"Binding '{binding.Name}' on '{prefabPath}' references a component outside the prefab root.");

                string identifier = ToMemberIdentifier(binding.Name);
                if (!identifiers.Add(identifier))
                    throw new InvalidOperationException($"Bindings on '{prefabPath}' generate duplicate member identifier '{identifier}'. Rename one binding.");
            }
        }

        /// <summary>
        /// 解析 Binder 当前所属的真实 Prefab 根节点，避免 Unity 6 Prefab Stage 外层 Canvas (Environment) 被 transform.root 误认为 Prefab 根节点。
        /// </summary>
        private static GameObject ResolvePrefabRoot(UIComponentBinder binder)
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (IsInsidePrefabStage(binder, prefabStage))
                return prefabStage.prefabContentsRoot;

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(binder.gameObject);
            if (instanceRoot != null)
                return instanceRoot;

            return binder.transform.root.gameObject;
        }

        /// <summary>
        /// 判断 Binder 是否属于当前 Prefab Stage 内容根节点；允许检测根节点自身和其子节点，但后续校验仍要求 Binder 位于根节点自身。
        /// </summary>
        private static bool IsInsidePrefabStage(UIComponentBinder binder, PrefabStage prefabStage)
        {
            if (binder == null || prefabStage == null || prefabStage.prefabContentsRoot == null)
                return false;

            Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
            return binder.gameObject == prefabStage.prefabContentsRoot || binder.transform.IsChildOf(prefabRoot);
        }

        /// <summary>
        /// 构建只包含强类型组件字段、绑定方法和解绑方法的 PanelBase 源码。
        /// </summary>
        private static string BuildPanelBaseSource(string panelName, IReadOnlyList<UIComponentBinding> bindings)
        {
            StringBuilder source = new StringBuilder(2048);
            source.AppendLine("namespace Xuan.Prometheus");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine($"    /// 由 UIKit 代码生成器根据 {panelName} Prefab 的 UIComponentBinder 自动生成。");
            source.AppendLine("    /// 本文件只保存强类型组件表，业务生命周期和配置应写在对应 Panel 脚本中。");
            source.AppendLine("    /// </summary>");
            source.AppendLine($"    public abstract class {panelName}Base : UIPanel");
            source.AppendLine("    {");

            foreach (UIComponentBinding binding in bindings)
            {
                string memberName = ToMemberIdentifier(binding.Name);
                string typeName = GetGlobalTypeName(binding.Component.GetType());
                source.AppendLine("        /// <summary>");
                source.AppendLine($"        /// 获取 Binder 中名为 {binding.Name} 的强类型组件引用。");
                source.AppendLine("        /// </summary>");
                source.AppendLine($"        protected {typeName} {memberName} {{ get; private set; }}");
                source.AppendLine();
            }

            foreach (UIComponentBinding binding in bindings.Where(IsButtonBinding))
            {
                string callbackName = GetButtonCallbackName(binding);
                source.AppendLine("        /// <summary>");
                source.AppendLine($"        /// 处理 {binding.Name} 的点击事件；按钮监听由生成基类自动注册和移除。");
                source.AppendLine("        /// </summary>");
                source.AppendLine($"        protected abstract void {callbackName}();");
                source.AppendLine();
            }

            source.AppendLine("        /// <summary>");
            source.AppendLine("        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为所有 Button 自动注册点击监听。");
            source.AppendLine("        /// </summary>");
            source.AppendLine("        protected override void BindComponents(UIComponentBinder binder)");
            source.AppendLine("        {");

            for (int index = 0; index < bindings.Count; index++)
            {
                UIComponentBinding binding = bindings[index];
                string memberName = ToMemberIdentifier(binding.Name);
                string typeName = GetGlobalTypeName(binding.Component.GetType());
                source.AppendLine($"            {memberName} = binder.Get<{typeName}>({index}, \"{EscapeString(binding.Name)}\");");
            }

            if (bindings.Any(IsButtonBinding))
                source.AppendLine();

            foreach (UIComponentBinding binding in bindings.Where(IsButtonBinding))
                source.AppendLine($"            {ToMemberIdentifier(binding.Name)}.onClick.AddListener({GetButtonCallbackName(binding)});");

            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        /// <summary>");
            source.AppendLine("        /// 在面板最终释放时移除全部 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。");
            source.AppendLine("        /// </summary>");
            source.AppendLine("        protected override void UnbindComponents()");
            source.AppendLine("        {");

            foreach (UIComponentBinding binding in bindings.Where(IsButtonBinding))
                source.AppendLine($"            {ToMemberIdentifier(binding.Name)}.onClick.RemoveListener({GetButtonCallbackName(binding)});");

            if (bindings.Any(IsButtonBinding))
                source.AppendLine();

            foreach (UIComponentBinding binding in bindings)
                source.AppendLine($"            {ToMemberIdentifier(binding.Name)} = null;");

            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        /// <summary>
        /// 构建仅在首次生成时创建的业务 Panel 模板，后续生成不会覆盖用户逻辑。
        /// </summary>
        private static string BuildPanelSource(string panelName, IReadOnlyList<UIComponentBinding> bindings)
        {
            StringBuilder source = new StringBuilder(1024);
            source.AppendLine("using UnityEngine;");
            source.AppendLine();
            source.AppendLine("namespace Xuan.Prometheus");
            source.AppendLine("{");
            source.AppendLine("    /// <summary>");
            source.AppendLine($"    /// {panelName} 的业务控制器；代码生成器只会首次创建本文件，不会覆盖后续业务修改。");
            source.AppendLine("    /// </summary>");
            source.AppendLine($"    [UIPanelConfig(\"Prefabs_{panelName}\", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]");
            source.AppendLine($"    public sealed class {panelName} : {panelName}Base");
            source.AppendLine("    {");
            source.AppendLine("        /// <summary>");
            source.AppendLine("        /// 每次面板进入显示状态时调用，可在此刷新界面数据。");
            source.AppendLine("        /// </summary>");
            source.AppendLine("        protected override void OnOpen()");
            source.AppendLine("        {");
            source.AppendLine($"            Debug.Log(\"[UIKit] {panelName} opened.\", Root);");
            source.AppendLine("        }");

            foreach (UIComponentBinding binding in bindings.Where(IsButtonBinding))
            {
                source.AppendLine();
                source.AppendLine("        /// <summary>");
                source.AppendLine($"        /// 响应 {binding.Name} 点击事件；监听注册和移除由生成的 {panelName}Base 自动管理。");
                source.AppendLine("        /// </summary>");
                source.AppendLine($"        protected override void {GetButtonCallbackName(binding)}()");
                source.AppendLine("        {");
                source.AppendLine("        }");
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        /// <summary>
        /// 判断绑定组件是否为 Unity Button 或其派生类型，这些组件需要生成自动点击监听代码。
        /// </summary>
        private static bool IsButtonBinding(UIComponentBinding binding)
        {
            return binding != null && binding.Component != null && typeof(UnityEngine.UI.Button).IsAssignableFrom(binding.Component.GetType());
        }

        /// <summary>
        /// 根据绑定成员名生成稳定的抽象点击回调名称，例如 BagButton 对应 OnBagButtonClick。
        /// </summary>
        private static string GetButtonCallbackName(UIComponentBinding binding)
        {
            return "On" + ToMemberIdentifier(binding.Name) + "Click";
        }

        /// <summary>
        /// 将绑定名称转换成可读的 PascalCase C# 成员标识符。
        /// </summary>
        private static string ToMemberIdentifier(string value)
        {
            return ToIdentifier(value, "Component");
        }

        /// <summary>
        /// 将 Prefab 名称转换成合法的 C# 类型标识符，并确保使用 Panel 后缀。
        /// </summary>
        private static string ToTypeIdentifier(string value)
        {
            string identifier = ToIdentifier(value, "GeneratedPanel");
            return identifier.EndsWith("Panel", StringComparison.Ordinal) ? identifier : identifier + "Panel";
        }

        /// <summary>
        /// 将任意名称的字母数字段转换为合法 PascalCase 标识符。
        /// </summary>
        private static string ToIdentifier(string value, string fallback)
        {
            string[] parts = new string(value.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder identifier = new StringBuilder();
            foreach (string part in parts)
            {
                identifier.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    identifier.Append(part.Substring(1));
            }

            if (identifier.Length == 0)
                identifier.Append(fallback);

            if (char.IsDigit(identifier[0]))
                identifier.Insert(0, '_');

            return identifier.ToString();
        }

        /// <summary>
        /// 获取可直接写入生成源码的全限定组件类型名。
        /// </summary>
        private static string GetGlobalTypeName(Type type)
        {
            string fullName = type.FullName ?? throw new InvalidOperationException($"Component type '{type}' does not have a full name and cannot be generated.");
            return "global::" + fullName.Replace('+', '.');
        }

        /// <summary>
        /// 转义生成的 C# 字符串字面量内容。
        /// </summary>
        private static string EscapeString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
