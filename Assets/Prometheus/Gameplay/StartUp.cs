using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xuan.Prometheus;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Prometheus.Gameplay
{
    [DefaultExecutionOrder(-99)]
    public class StartUp : MonoBehaviour
    {
        private static PlayerEntity player;
        private static List<SlimeEntity> enemies = new();
        public EnemySpawnConfig enemySpawnConfig;
        private void Awake()
        {
            // Application.targetFrameRate = 999;
            player = new PlayerEntity(GameObject.Find("Yefa"));
            // FillField<DataAttribute, IComponent>(player, player.AddComp);
            // FillField<LogicAttribute, ILogic>(player, player.AddLogic);
            player.AfterNew();
            var slime = GameObject.Find("Slime");
            foreach (var p in enemySpawnConfig.spawnPoints)
            {
                var enemy = new SlimeEntity(Instantiate(slime, p));
                enemy.AfterNew();
                enemies.Add(enemy);
            }
            Destroy(slime);
        }

        private void Update()
        {
            player.OnUpdate(Time.deltaTime);
            foreach (var enemy in enemies)
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