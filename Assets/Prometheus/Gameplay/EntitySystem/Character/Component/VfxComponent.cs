using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>定义 Yefa 各主动动作使用的稳定特效槽位语义。</summary>
    public enum YefaVfx
    {
        /// <summary>第一段普通攻击特效。</summary>
        Atk1,
        /// <summary>第二段普通攻击特效。</summary>
        Atk2,
        /// <summary>第三段普通攻击特效。</summary>
        Atk3,
        /// <summary>第四段普通攻击特效。</summary>
        Atk4,
        /// <summary>特殊攻击特效。</summary>
        Special,
        /// <summary>技能特效。</summary>
        Skill,
        /// <summary>大招特效。</summary>
        Ult
    }

    /// <summary>集中管理角色动作特效槽位的播放、停止和对象停用清理，避免父对象重新激活时恢复旧特效。</summary>
    public class VfxComponent : Component.Component, Component.IEntityBinderComponent
    {
        /// <summary>按照 YefaVfx 枚举顺序保存角色预制体上的特效根对象。</summary>
        public List<GameObject> vfxSlots = new List<GameObject>();

        /// <summary>从唯一根 CharacterBinder 复制动作特效槽位引用。</summary>
        public void Bind(Logic.EntityBinder binder)
        {
            CharacterBinder characterBinder = binder as CharacterBinder ?? throw new System.InvalidOperationException($"VfxComponent requires CharacterBinder but received '{binder?.GetType().FullName}'.");
            vfxSlots = characterBinder.VfxSlots == null ? new List<GameObject>() : new List<GameObject>(characterBinder.VfxSlots);
        }

        /// <summary>停止全部动作特效并解除槽位引用。</summary>
        public void Unbind()
        {
            StopAll();
            vfxSlots.Clear();
        }

        /// <summary>重新激活指定特效槽；配置缺失时记录警告并保持玩法流程运行。</summary>
        public void Play(YefaVfx vfx)
        {
            if (!TryGetSlot(vfx, out GameObject slot, true)) return;
            slot.SetActive(false);
            slot.SetActive(true);
        }

        /// <summary>停止指定动作特效并把槽位恢复为非激活状态；重复停止保持幂等。</summary>
        public void Stop(YefaVfx vfx)
        {
            if (!TryGetSlot(vfx, out GameObject slot, false)) return;
            slot.SetActive(false);
        }

        /// <summary>停止全部动作特效，供换人、对象池回收和角色根对象停用时清除视觉运行态。</summary>
        public void StopAll()
        {
            if (vfxSlots == null) return;
            for (int index = 0; index < vfxSlots.Count; index++)
            {
                GameObject slot = vfxSlots[index];
                if (slot != null) slot.SetActive(false);
            }
        }

        /// <summary>按枚举索引安全解析特效槽，并按调用场景决定是否报告缺失配置。</summary>
        private bool TryGetSlot(YefaVfx vfx, out GameObject slot, bool logMissingConfiguration)
        {
            int index = (int)vfx;
            if (vfxSlots != null && index >= 0 && index < vfxSlots.Count && vfxSlots[index] != null)
            {
                slot = vfxSlots[index];
                return true;
            }
            slot = null;
            if (logMissingConfiguration) Debug.LogWarning($"VFX 槽位 '{vfx}' 未配置。");
            return false;
        }
    }
}
