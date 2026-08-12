namespace Xuan.Prometheus
{
    /// <summary>
    /// 由 UIKit 代码生成器根据 HudPanel Prefab 的 UIComponentBinder 自动生成。
    /// 本文件只保存强类型组件表，业务生命周期和配置应写在对应 Panel 脚本中。
    /// </summary>
    public abstract class HudPanelBase : UIPanel
    {
        /// <summary>
        /// 获取 Binder 中名为 LotteryButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button LotteryButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 StickButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button StickButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 UltButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button UltButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 MiniMapButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button MiniMapButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 QuestButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button QuestButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 MenuButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button MenuButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 JumpButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button JumpButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 AtkButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button AtkButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 DodgeButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button DodgeButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 SkillButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button SkillButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 WalkButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button WalkButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 RunButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button RunButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 GuideButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button GuideButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 EventButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button EventButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 CharacterButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button CharacterButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 BagButton 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button BagButton { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 Hp 的强类型组件引用。
        /// </summary>
        protected global::TMPro.TextMeshProUGUI Hp { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 HpBar 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image HpBar { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 EnergyFrame 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image EnergyFrame { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 EnergyImg 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Image EnergyImg { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 Ult 的强类型组件引用。
        /// </summary>
        protected global::Xuan.Prometheus.UltMono Ult { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 BuffList 的强类型组件引用。
        /// </summary>
        protected global::SuperScrollView.LoopListView2 BuffList { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 Skill 的强类型组件引用。
        /// </summary>
        protected global::Xuan.Prometheus.CdMono Skill { get; private set; }

        /// <summary>
        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为没有接入 Input System 屏幕控件的 Button 注册点击监听。
        /// </summary>
        protected override void BindComponents(UIComponentBinder binder)
        {
            LotteryButton = binder.Get<global::UnityEngine.UI.Button>(0, "LotteryButton");
            StickButton = binder.Get<global::UnityEngine.UI.Button>(1, "StickButton");
            UltButton = binder.Get<global::UnityEngine.UI.Button>(2, "UltButton");
            MiniMapButton = binder.Get<global::UnityEngine.UI.Button>(3, "MiniMapButton");
            QuestButton = binder.Get<global::UnityEngine.UI.Button>(4, "QuestButton");
            MenuButton = binder.Get<global::UnityEngine.UI.Button>(5, "MenuButton");
            JumpButton = binder.Get<global::UnityEngine.UI.Button>(6, "JumpButton");
            AtkButton = binder.Get<global::UnityEngine.UI.Button>(7, "AtkButton");
            DodgeButton = binder.Get<global::UnityEngine.UI.Button>(8, "DodgeButton");
            SkillButton = binder.Get<global::UnityEngine.UI.Button>(9, "SkillButton");
            WalkButton = binder.Get<global::UnityEngine.UI.Button>(10, "WalkButton");
            RunButton = binder.Get<global::UnityEngine.UI.Button>(11, "RunButton");
            GuideButton = binder.Get<global::UnityEngine.UI.Button>(12, "GuideButton");
            EventButton = binder.Get<global::UnityEngine.UI.Button>(13, "EventButton");
            CharacterButton = binder.Get<global::UnityEngine.UI.Button>(14, "CharacterButton");
            BagButton = binder.Get<global::UnityEngine.UI.Button>(15, "BagButton");
            Hp = binder.Get<global::TMPro.TextMeshProUGUI>(16, "Hp");
            HpBar = binder.Get<global::UnityEngine.UI.Image>(17, "HpBar");
            EnergyFrame = binder.Get<global::UnityEngine.UI.Image>(18, "EnergyFrame");
            EnergyImg = binder.Get<global::UnityEngine.UI.Image>(19, "EnergyImg");
            Ult = binder.Get<global::Xuan.Prometheus.UltMono>(20, "Ult");
            BuffList = binder.Get<global::SuperScrollView.LoopListView2>(21, "BuffList");
            Skill = binder.Get<global::Xuan.Prometheus.CdMono>(22, "Skill");
        }

        /// <summary>
        /// 在面板最终释放时移除生成器托管的 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。
        /// </summary>
        protected override void UnbindComponents()
        {
            LotteryButton = null;
            StickButton = null;
            UltButton = null;
            MiniMapButton = null;
            QuestButton = null;
            MenuButton = null;
            JumpButton = null;
            AtkButton = null;
            DodgeButton = null;
            SkillButton = null;
            WalkButton = null;
            RunButton = null;
            GuideButton = null;
            EventButton = null;
            CharacterButton = null;
            BagButton = null;
            Hp = null;
            HpBar = null;
            EnergyFrame = null;
            EnergyImg = null;
            Ult = null;
            BuffList = null;
            Skill = null;
        }
    }
}
