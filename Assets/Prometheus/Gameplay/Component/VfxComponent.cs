using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    public enum YefaVfx
    {
        Atk1,
        Atk2,
        Atk3,
        Atk4,
        Special,
        Skill,
        Ult
    }
    public class VfxComponent : Component.MonoComponent
    {
        public List<GameObject> vfxSlots;
        /// <summary>重新激活指定特效槽；配置缺失时记录警告并保持玩法流程运行。</summary>
        public void Play(YefaVfx vfx)
        {
            int index = (int)vfx;
            if (vfxSlots == null || index < 0 || index >= vfxSlots.Count || vfxSlots[index] == null)
            {
                Debug.LogWarning($"VFX 槽位 '{vfx}' 未配置。", this);
                return;
            }
            vfxSlots[index].SetActive(false);
            vfxSlots[index].SetActive(true);
        }
    }
}
