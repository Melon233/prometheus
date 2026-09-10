using TMPro;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>
    /// 原型阶段统一使用的占位配色与占位美术。
    /// 这里刻意只使用纯色和 Unity 内置图集，不引用工程内任何美术资产，避免原型阶段产生资源引用与打包依赖。
    /// </summary>
    public static class UIProtoTheme
    {
        /// <summary>全屏遮罩色。</summary>
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>主面板底色。</summary>
        public static readonly Color PanelBg = new Color32(0xEC, 0xE5, 0xD8, 0xFF);

        /// <summary>次级面板底色，用于详情等内层区域。</summary>
        public static readonly Color PanelBgAlt = new Color32(0xF6, 0xF2, 0xE8, 0xFF);

        /// <summary>深色承载面底色，用于侧边栏这类与主面板反差的区域。</summary>
        public static readonly Color SurfaceDark = new Color32(0x2A, 0x31, 0x40, 0xFF);

        /// <summary>信息卡片底色，用于角色资料这类需要与列表区分的头部区域。</summary>
        public static readonly Color ProfileBg = new Color32(0xBC, 0xD4, 0xE8, 0xFF);

        /// <summary>列表项默认底色。</summary>
        public static readonly Color ItemBg = new Color32(0xDE, 0xD6, 0xC5, 0xFF);

        /// <summary>列表项选中底色。</summary>
        public static readonly Color ItemBgSelected = new Color32(0xC6, 0xB9, 0x9C, 0xFF);

        /// <summary>主要文本色。</summary>
        public static readonly Color TextPrimary = new Color32(0x49, 0x52, 0x66, 0xFF);

        /// <summary>次要文本色。</summary>
        public static readonly Color TextSecondary = new Color32(0x8B, 0x84, 0x77, 0xFF);

        /// <summary>反白文本色，用于深色按钮。</summary>
        public static readonly Color TextInverse = new Color32(0xF6, 0xF2, 0xE8, 0xFF);

        /// <summary>强调金色。</summary>
        public static readonly Color Accent = new Color32(0xD3, 0xBC, 0x8E, 0xFF);

        /// <summary>主行动按钮底色。</summary>
        public static readonly Color ButtonPrimary = new Color32(0x49, 0x52, 0x66, 0xFF);

        /// <summary>次级按钮底色。</summary>
        public static readonly Color ButtonSecondary = new Color32(0xCB, 0xC2, 0xAF, 0xFF);

        /// <summary>分隔线颜色。</summary>
        public static readonly Color Divider = new Color32(0xC8, 0xBF, 0xAB, 0xFF);

        /// <summary>一星品质底色。</summary>
        public static readonly Color QualityGray = new Color32(0x8A, 0x8F, 0x99, 0xFF);

        /// <summary>二星品质底色。</summary>
        public static readonly Color QualityGreen = new Color32(0x5C, 0x9E, 0x6B, 0xFF);

        /// <summary>三星品质底色。</summary>
        public static readonly Color QualityBlue = new Color32(0x4E, 0x86, 0xC7, 0xFF);

        /// <summary>四星品质底色。</summary>
        public static readonly Color QualityPurple = new Color32(0x96, 0x69, 0xC2, 0xFF);

        /// <summary>五星品质底色。</summary>
        public static readonly Color QualityGold = new Color32(0xD2, 0x9A, 0x35, 0xFF);

        /// <summary>按星级从低到高排列的品质底色，索引即品质等级减一。</summary>
        public static readonly Color[] Qualities = { QualityGray, QualityGreen, QualityBlue, QualityPurple, QualityGold };

        /// <summary>未读或警示标记色。</summary>
        public static readonly Color Alert = new Color32(0xD9, 0x54, 0x4B, 0xFF);

        /// <summary>获取 Unity 内置的九宫格圆角底图，用于面板和按钮占位。</summary>
        public static Sprite SlicedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        /// <summary>获取 Unity 内置的圆形底图，用于圆点和头像占位。</summary>
        public static Sprite RoundSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        /// <summary>原型字体资产路径。工程约定所有界面文本统一使用同一款字体，因此这里不提供字重选择。</summary>
        private const string FontAssetPath = "Assets/BundleResources/Font/HYWenHei-85W SDF.asset";

        /// <summary>
        /// 解析原型文本使用的字体资产；资产缺失时回退 TMP 全局默认字体，保证原型仍能构建。
        /// </summary>
        public static TMP_FontAsset ResolveFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogWarning("[UIProto] Font asset '" + FontAssetPath + "' is missing; falling back to the TMP default font asset.");
                font = TMP_Settings.defaultFontAsset;
            }

            RequirePersistentMaterial(font);
            return font;
        }

        /// <summary>
        /// 确认字体资产的材质已经作为子资产落盘。
        /// 通过脚本创建的 TMP 字体资产，其材质默认只是内存对象；一旦被 Prefab 引用，域重载后引用即变为 null，
        /// 表现为文字完全不渲染而没有任何报错，因此在构建期直接拦截。
        /// </summary>
        private static void RequirePersistentMaterial(TMP_FontAsset font)
        {
            if (font == null) throw new System.InvalidOperationException("[UIProto] No font asset available; assign a Default Font Asset in TMP Settings.");
            if (font.material == null)
                throw new System.InvalidOperationException("[UIProto] Font asset '" + font.name + "' has no material. Recreate it through the TextMeshPro Font Asset Creator.");

            if (!AssetDatabase.Contains(font.material))
                throw new System.InvalidOperationException("[UIProto] Font asset '" + font.name + "' has a material that is not saved as a sub-asset, so prefabs referencing it would render no text after a domain reload. Add the material to the font asset before building prototypes.");
        }
    }
}
