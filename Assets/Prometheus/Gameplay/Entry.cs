using UnityEngine;

namespace Xuan.Prometheus
{
    public class Entry : MonoBehaviour
    {
        private void Start()
        {
            var ioc = new Ioc();
            ioc.Register<IEventKit>(new EventKit());
            ioc.Register<IStaticEventKit>(new StaticEventKit());
            ioc.Register<IFsmKit>(new FsmKit());
            ioc.Register<IAssetKit>(new AssetKit());
            ioc.Register<IUIKit>(new UIKit());
        }

    }
}