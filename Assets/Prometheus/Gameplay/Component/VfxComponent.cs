
using System.Collections.Generic;
using log4net.Util;
using UnityEngine;

namespace Xuan.Prometheus
{
    public enum YefaVfx
    {
        Atk1,
        Atk2,
        Atk3,
        Atk4,
        Skill,
        Ult
    }
    public class VfxComponent : Component.MonoComponent
    {
        public List<GameObject> vfxSlots;
        public void Play(YefaVfx vfx)
        {
            vfxSlots[(int)vfx].SetActive(false);
            vfxSlots[(int)vfx].SetActive(true);
        }
    }
}