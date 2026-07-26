using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public enum LogicTag
    {
        GroundMove,
        Atk,
        Jump,
        AirMove,
        Rotate,
        Dodge
    }

    public enum LogicGroup
    {
        Input,
        Talent,
        Buff,
        Gameplay,
        Bag,
        Controller,
        Formation,
        Build,
        Item,
        Quest,
        Compound,
        AfterGameplay
    }

    public class LogicAttribute : Attribute
    {
    }


    public class DataAttribute : Attribute
    {
    }

    public abstract class Entity
    {
        public GameObject bindGo;
        protected Dictionary<Type, IComponent> comps = new();
        protected List<ILogic> logicList = new();
        protected Dictionary<Type, ILogic> logics = new();
        protected List<IComponent> toAddComps = new();
        protected List<ILogic> toAddLogics = new();
        protected List<Type> toRemoveComps = new();
        protected List<Type> toRemoveLogics = new();
        protected bool toDispose = false;
        protected float delay = 1f;
        public void AfterNew()
        {
            logicList = logics.Values.ToList();
            logicList.Sort((a, b) => a.LogicGroup.CompareTo(b.LogicGroup));
            foreach (var logic in logicList) logic.AfterNew();
        }

        public void OnUpdate(float dt)
        {
            if (toDispose)
            {
                Dispose();
                return;
            }

            // if (toAddComps.Count != 0)
            // {
            //     foreach (var comp in toAddComps) comps.Add(comp.GetType(), comp);
            //     toAddComps.Clear();
            // }

            // if (toAddLogics.Count != 0)
            // {
            //     foreach (var logic in toAddLogics)
            //     {
            //         logics.Add(logic.GetType(), logic);
            //         logicList.Add(logic);
            //         logic.AfterNew();
            //     }

            //     toAddLogics.Clear();
            // }

            // if (toRemoveComps.Count != 0)
            // {
            //     foreach (var type in toRemoveComps)
            //         comps.Remove(type);
            //     toRemoveComps.Clear();
            // }

            // if (toRemoveLogics.Count != 0)
            // {
            //     foreach (var type in toRemoveLogics)
            //         if (logics.TryGetValue(type, out var logic))
            //         {
            //             logic.OnDispose();
            //             logics.Remove(type);
            //         }


            //     toRemoveLogics.Clear();
            // }

            logicList.Sort((a, b) => a.LogicGroup.CompareTo(b.LogicGroup));
            foreach (var logic in logicList)
            {
                CheckLogic(logic);
                if (logic.Enable && logic.BlockCnt == 0) logic.OnUpdate(dt);
            }
        }

        public void OnDispose(float delay = 2f)
        {
            this.delay = delay;
            toDispose = true;
        }
        void Dispose()
        {
            foreach (var logic in logicList) logic.OnDispose();
            comps.Clear();
            logicList.Clear();
            logics.Clear();
            GameObject.Destroy(bindGo, delay);
        }
        public void CheckLogic<T>(T logic) where T : ILogic
        {
            if (!logic.Enable && logic.BlockCnt == 0 && logic.CanEnable())
            {
                logic.Enable = true;
                logic.OnEnable();
            }
            else if (logic.Enable && (logic.BlockCnt != 0 || logic.CanDisable()))
            {
                logic.Enable = false;
                logic.OnDisable();
            }
        }
        public void AddLogic<T>() where T : ILogic, new()
        {
            var logic = new T();
            logics.Add(typeof(T), logic);
            logic.Entity = this;
            // typeof(T).GetTypeInfo().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            //     .ToList().ForEach(f =>
            //     {
            //         if (f.GetCustomAttribute<InjectDataAttribute>() != null) f.SetValue(logic, GetComp(f.FieldType));
            //     });
        }

        public void AddLogic<T>(T logic) where T : ILogic
        {
            logics.Add(logic.GetType(), logic);
            logic.Entity = this;
            // logic.GetType().GetTypeInfo()
            //     .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            //     .ToList().ForEach(f =>
            //     {
            //         if (f.GetCustomAttribute<InjectDataAttribute>() != null) f.SetValue(logic, GetComp(f.FieldType));
            //     });
        }

        public void AddComp<T>(T comp) where T : IComponent
        {
            comps.Add(comp.GetType(), comp);
            comp.Entity = this;
        }

        public void AddComp<T>() where T : IComponent, new()
        {
            var comp = new T();
            comps.Add(comp.GetType(), comp);
            comp.Entity = this;
        }

        public bool TryGetComp<T>(out T comp) where T : IComponent
        {
            if (comps.TryGetValue(typeof(T), out var dat))
            {
                comp = (T)dat;
                return true;
            }
            Debug.LogError($"{GetType()} Can't find comp {typeof(T)}");
            comp = default;
            return false;
        }

        // public IComponent GetComp(Type type)
        // {
        //     if (comps.TryGetValue(type, out var dat)) return dat;

        //     return null;
        // }

        // public void AddLogicRuntime<T>() where T : ILogic, new()
        // {
        //     var logic = new T();
        //     toAddLogics.Add(logic);
        //     logic.Entity = this;
        // }

        // public void AddCompRuntime<T>() where T : IComponent, new()
        // {
        //     var comp = new T();
        //     toAddComps.Add(comp);
        //     comp.Entity = this;
        // }

        // public void RemoveLogic<T>() where T : ILogic
        // {
        //     toRemoveLogics.Add(typeof(T));
        // }

        // public void RemoveComp<T>() where T : IComponent
        // {
        //     toRemoveComps.Add(typeof(T));
        // }
        // public void AddConfig<T>() where T : ScriptableObject
        // {
        // }

        // public void BlockTag(LogicTag logicTag, object blocker)
        // {
        //     if (!blockRegis.ContainsKey(logicTag)) blockRegis[logicTag] = new HashSet<object>();
        //     blockRegis[logicTag].Add(blocker);
        // }

        // public void UnBlockTag(LogicTag logicTag, object blocker)
        // {
        //     if (blockRegis.ContainsKey(logicTag)) blockRegis[logicTag].Remove(blocker);
        //     if (blockRegis[logicTag].Count == 0) blockRegis.Remove(logicTag);
        // }

        // public void BlockLogic<T>(object blocker) where T : ILogic
        // {
        //     var logic = logics[typeof(T)];
        //     logic.BlockCnt++;
        //     CheckLogic(logic);
        // }

        // public void UnBlockLogic<T>(object blocker) where T : ILogic
        // {
        //     var logic = logics[typeof(T)];
        //     logic.BlockCnt--;
        //     CheckLogic(logic);
        // }

        public void BlockLogic<T>() where T : ILogic
        {
            var logic = logics[typeof(T)];
            logic.BlockCnt++;
            // CheckLogic(logic);
        }

        public void UnBlockLogic<T>() where T : ILogic
        {
            var logic = logics[typeof(T)];
            logic.BlockCnt = --logic.BlockCnt < 0 ? 0 : logic.BlockCnt;
            // CheckLogic(logic);
        }

        // public bool HasBlockTag(LogicTag logicTag)
        // {
        //     return blockRegis.ContainsKey(logicTag);
        // }

        // public bool HasLogic<T>() where T : ILogic
        // {
        //     return logics.ContainsKey(typeof(T));
        // }
    }
}