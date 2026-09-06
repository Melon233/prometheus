using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative.Demo
{
    /// <summary>
    /// 演示剧情使用的文案表。
    /// 正式流程由表格导出工具生成 TextMapAsset；此处直接在代码中构建，使演示场景不依赖任何外部资产。
    /// </summary>
    public static class DemoTexts
    {
        /// <summary>演示使用的语言代码。</summary>
        public const string Language = "zh-CN";

        /// <summary>构建一个已载入演示文案的运行时文本表。</summary>
        public static TextMap CreateTextMap()
        {
            TextMap textMap = new TextMap();
            LoadInto(textMap);
            return textMap;
        }

        /// <summary>把演示文案并入一个已有的文本表。</summary>
        public static void LoadInto(TextMap textMap)
        {
            if (textMap == null) throw new System.ArgumentNullException(nameof(textMap));
            textMap.Load(Language, BuildEntries());
        }

        /// <summary>枚举全部演示文案条目。</summary>
        private static IEnumerable<KeyValuePair<string, string>> BuildEntries()
        {
            // 角色显示名：约定键为 actor.<id>.name，由 ActorRef.DisplayNameKey 解析。
            yield return Entry("actor.hero.name", "荧");
            yield return Entry("actor.paimon.name", "派蒙");
            yield return Entry("actor.elder.name", "长老");

            yield return Entry("demo.intro", "风停了。石阶尽头的祭坛还亮着，像是有人刚刚离开。");
            yield return Entry("demo.p1", "哇——这里就是地图上标记的那座祭坛吗？看起来好久没人打理了。");
            yield return Entry("demo.h1", "等一下，那边好像有人。");
            yield return Entry("demo.h1_hint", "（转身走近，脚步声在石阶上回响。）");
            yield return Entry("demo.e1", "来者止步。这座祭坛不欢迎带着武器的旅人……不过，你们身上没有敌意。");
            yield return Entry("demo.e2", "风？风早就不属于这里了。它被人带走的那天，祭坛的火就熄了一半。");
            yield return Entry("demo.e3", "既然无话可说，那就请回吧。");
            yield return Entry("demo.e_secret", "……你竟然知道那个名字。看来我该重新估量你了。");
            yield return Entry("demo.h2", "这里发生过什么？为什么风会停？");
            yield return Entry("demo.p2", "原来是这样……派蒙记下了！这条线索说不定有用。");
            yield return Entry("demo.p3", "唔……什么都没问到，就这么走了真的好吗？");
            yield return Entry("demo.outro", "祭坛的火重新亮了一格。");

            yield return Entry("demo.opt_ask", "这里发生过什么？");
            yield return Entry("demo.opt_secret", "提起「西风的旧名」");
            yield return Entry("demo.opt_secret_locked", "信任不足");
            yield return Entry("demo.opt_leave", "没什么想问的");
        }

        /// <summary>创建一条文案条目。</summary>
        private static KeyValuePair<string, string> Entry(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
