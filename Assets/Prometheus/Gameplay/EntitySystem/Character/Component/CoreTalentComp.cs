namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>保存 TalentLogic 已验证的角色统一 TalentConfig，供跨能力天赋组合读取。</summary>
    public class CoreTalentComponent : Component.Component
    {
        /// <summary>获取当前角色全部能力共享的只读数值配置。</summary>
        public TalentConfig TalentConfig { get; private set; }

        /// <summary>由 TalentLogic 在初始化阶段绑定已经通过一致性检查的配置。</summary>
        public void BindConfig(TalentConfig config)
        {
            TalentConfig = config;
        }
    }
}
