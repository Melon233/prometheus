using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Prometheus.Gameplay
{
    public class StartUp : MonoBehaviour
    {
        private static PlayerEntity player;
        private static SlimeEntity enemy;

        private void Start()
        {
            Application.targetFrameRate = 999;
            player = new PlayerEntity(GameObject.Find("Yefa"));
            // FillField<DataAttribute, IComponent>(player, player.AddComp);
            // FillField<LogicAttribute, ILogic>(player, player.AddLogic);
            player.AfterNew();
            enemy = new SlimeEntity(GameObject.Find("Slime"));
            enemy.AfterNew();
        }

        private void Update()
        {
            player.OnUpdate(Time.deltaTime);
            enemy.OnUpdate(Time.deltaTime);
        }

        // public void FillField<TAbbr, TField>(Entity obj, Action<TField> callback = null)
        //     where TAbbr : Attribute
        // {
        //     obj.GetType().GetTypeInfo().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
        //         .ToList().ForEach(f =>
        //         {
        //             var abbr = f.GetCustomAttribute(typeof(TAbbr));
        //             if (abbr != null)
        //             {
        //                 if (f.FieldType.IsSubclassOf(typeof(MonoBehaviour)))
        //                     f.SetValue(obj, obj.bindGo?.GetComponent(f.FieldType));
        //                 else
        //                     f.SetValue(obj, Activator.CreateInstance(f.FieldType));
        //                 callback?.Invoke((TField)f.GetValue(obj));
        //             }
        //         });
        // }
    }
}