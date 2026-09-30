using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情系统内置界面的运行时构建工具。
    /// <para>
    /// 使用 uGUI 的 <see cref="Text"/> 与系统动态字体，因为工程内的 TMP 字体资产不含中文字形。
    /// 正式对话界面应改用带中文字形的 TMP 字体资产并走 UIKit 的预制体流程；
    /// 本工具服务于黑幕、黑边等与美术无关的基础层，以及演示与测试场景。
    /// </para>
    /// </summary>
    public static class NarrativeUiFactory
    {
        /// <summary>按优先级尝试的系统字体名，用于保证中文正常显示。</summary>
        public static readonly string[] DefaultFonts = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Hiragino Sans GB", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" };

        /// <summary>解析一个包含中文字形的动态字体；系统字体不可用时退回内置字体。</summary>
        public static Font ResolveFont(string[] preferred = null)
        {
            string[] candidates = preferred != null && preferred.Length > 0 ? preferred : DefaultFonts;
            Font osFont = Font.CreateDynamicFontFromOSFont(candidates, 32);
            if (osFont != null) return osFont;
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>创建一个带 RectTransform 的界面节点。</summary>
        public static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            return created;
        }

        /// <summary>创建一个覆盖式画布。</summary>
        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder)
        {
            GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        /// <summary>创建一个使用指定字体的文本节点。</summary>
        public static Text CreateText(Transform parent, Font font, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = CreateUiObject("Text", parent);
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>创建一个带背景与文字的按钮。</summary>
        public static Button CreateButton(Transform parent, Font font, string label, Color background, UnityAction onClick)
        {
            GameObject buttonObject = CreateUiObject("Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = background;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText(buttonObject.transform, font, 20, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform, 10f, 4f);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>把一个矩形拉伸铺满父节点并留出内边距。</summary>
        public static void Stretch(RectTransform rect, float horizontal, float vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        /// <summary>确保场景中存在支持新版输入系统的事件系统，使按钮可以被点击。</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
