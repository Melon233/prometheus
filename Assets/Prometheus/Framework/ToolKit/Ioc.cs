using System;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

namespace Xuan.Prometheus
{
    public abstract class Kit
    {
        public IIoc Ioc { get; set; }  // Ioc接口
    }
    public interface IIoc
    {
        void Register<T>(T singleton);
        T Get<T>();
        UIKit UIKit { get; }  // UIKit接口
        EventKit EventKit { get; }  // EventKit接口
        StaticEventKit StaticEventKit { get; }  // StaticEventKit接口
        FsmKit FsmKit { get; }  // FsmKit接口
        AssetKit AssetKit { get; }  // AssetKit接口
    }
    public class Ioc : IIoc
    {
        private Dictionary<Type, object> objDict = new Dictionary<Type, object>();
        public UIKit UIKit => Get<UIKit>();

        public EventKit EventKit => Get<EventKit>();

        public StaticEventKit StaticEventKit => Get<StaticEventKit>();

        public FsmKit FsmKit => Get<FsmKit>();

        public AssetKit AssetKit => Get<AssetKit>();  // 获取实例

        public T Get<T>()
        {
            if (objDict.ContainsKey(typeof(T)))
            {
                return (T)objDict[typeof(T)];
            }
            throw new Exception($"未找到类型 {typeof(T)} 的实例");  // 未找到实例时抛出异常
        }

        public void Register<T>(T singleton)
        {
            objDict.Add(typeof(T), singleton);
        }
    }
}