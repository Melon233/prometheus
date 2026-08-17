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
        /// 获取 Binder 中名为 Avatar1 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button Avatar1 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 Avatar2 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button Avatar2 { get; private set; }

        /// <summary>
        /// 获取 Binder 中名为 Avatar3 的强类型组件引用。
        /// </summary>
        protected global::UnityEngine.UI.Button Avatar3 { get; private set; }

        /// <summary>
        /// 处理 LotteryButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnLotteryButtonClick();

        /// <summary>
        /// 处理 UltButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnUltButtonClick();

        /// <summary>
        /// 处理 MiniMapButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnMiniMapButtonClick();

        /// <summary>
        /// 处理 QuestButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnQuestButtonClick();

        /// <summary>
        /// 处理 MenuButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnMenuButtonClick();

        /// <summary>
        /// 处理 JumpButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnJumpButtonClick();

        /// <summary>
        /// 处理 AtkButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnAtkButtonClick();

        /// <summary>
        /// 处理 DodgeButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnDodgeButtonClick();

        /// <summary>
        /// 处理 SkillButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnSkillButtonClick();

        /// <summary>
        /// 处理 GuideButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnGuideButtonClick();

        /// <summary>
        /// 处理 EventButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnEventButtonClick();

        /// <summary>
        /// 处理 CharacterButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnCharacterButtonClick();

        /// <summary>
        /// 处理 BagButton 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnBagButtonClick();

        /// <summary>
        /// 处理 Avatar1 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnAvatar1Click();

        /// <summary>
        /// 处理 Avatar2 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnAvatar2Click();

        /// <summary>
        /// 处理 Avatar3 的点击事件；按钮监听由生成基类自动注册和移除。
        /// </summary>
        protected abstract void OnAvatar3Click();

        /// <summary>
        /// 按 Binder 表中的稳定索引和名称绑定全部强类型组件字段，并为普通 Button 注册点击监听；承担拖拽输入的 OnScreenStick 不注册点击回调。
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
            GuideButton = binder.Get<global::UnityEngine.UI.Button>(10, "GuideButton");
            EventButton = binder.Get<global::UnityEngine.UI.Button>(11, "EventButton");
            CharacterButton = binder.Get<global::UnityEngine.UI.Button>(12, "CharacterButton");
            BagButton = binder.Get<global::UnityEngine.UI.Button>(13, "BagButton");
            Hp = binder.Get<global::TMPro.TextMeshProUGUI>(14, "Hp");
            HpBar = binder.Get<global::UnityEngine.UI.Image>(15, "HpBar");
            EnergyFrame = binder.Get<global::UnityEngine.UI.Image>(16, "EnergyFrame");
            EnergyImg = binder.Get<global::UnityEngine.UI.Image>(17, "EnergyImg");
            Ult = binder.Get<global::Xuan.Prometheus.UltMono>(18, "Ult");
            BuffList = binder.Get<global::SuperScrollView.LoopListView2>(19, "BuffList");
            Skill = binder.Get<global::Xuan.Prometheus.CdMono>(20, "Skill");
            Avatar1 = binder.Get<global::UnityEngine.UI.Button>(21, "Avatar1");
            Avatar2 = binder.Get<global::UnityEngine.UI.Button>(22, "Avatar2");
            Avatar3 = binder.Get<global::UnityEngine.UI.Button>(23, "Avatar3");

            LotteryButton.onClick.AddListener(OnLotteryButtonClick);
            UltButton.onClick.AddListener(OnUltButtonClick);
            MiniMapButton.onClick.AddListener(OnMiniMapButtonClick);
            QuestButton.onClick.AddListener(OnQuestButtonClick);
            MenuButton.onClick.AddListener(OnMenuButtonClick);
            JumpButton.onClick.AddListener(OnJumpButtonClick);
            AtkButton.onClick.AddListener(OnAtkButtonClick);
            DodgeButton.onClick.AddListener(OnDodgeButtonClick);
            SkillButton.onClick.AddListener(OnSkillButtonClick);
            GuideButton.onClick.AddListener(OnGuideButtonClick);
            EventButton.onClick.AddListener(OnEventButtonClick);
            CharacterButton.onClick.AddListener(OnCharacterButtonClick);
            BagButton.onClick.AddListener(OnBagButtonClick);
            Avatar1.onClick.AddListener(OnAvatar1Click);
            Avatar2.onClick.AddListener(OnAvatar2Click);
            Avatar3.onClick.AddListener(OnAvatar3Click);
        }

        /// <summary>
        /// 在面板最终释放时移除生成器托管的 Button 点击监听并清空组件引用，避免事件或控制器延长 Unity 对象生命周期。
        /// </summary>
        protected override void UnbindComponents()
        {
            LotteryButton.onClick.RemoveListener(OnLotteryButtonClick);
            UltButton.onClick.RemoveListener(OnUltButtonClick);
            MiniMapButton.onClick.RemoveListener(OnMiniMapButtonClick);
            QuestButton.onClick.RemoveListener(OnQuestButtonClick);
            MenuButton.onClick.RemoveListener(OnMenuButtonClick);
            JumpButton.onClick.RemoveListener(OnJumpButtonClick);
            AtkButton.onClick.RemoveListener(OnAtkButtonClick);
            DodgeButton.onClick.RemoveListener(OnDodgeButtonClick);
            SkillButton.onClick.RemoveListener(OnSkillButtonClick);
            GuideButton.onClick.RemoveListener(OnGuideButtonClick);
            EventButton.onClick.RemoveListener(OnEventButtonClick);
            CharacterButton.onClick.RemoveListener(OnCharacterButtonClick);
            BagButton.onClick.RemoveListener(OnBagButtonClick);
            Avatar1.onClick.RemoveListener(OnAvatar1Click);
            Avatar2.onClick.RemoveListener(OnAvatar2Click);
            Avatar3.onClick.RemoveListener(OnAvatar3Click);

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
            Avatar1 = null;
            Avatar2 = null;
            Avatar3 = null;
        }
    }
}
