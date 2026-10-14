using System;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 标在 [SerializeReference] 字段（或列表字段）上，让 Inspector 为每个元素画出「选择具体子类」的下拉按钮。
    /// Unity 对 SerializeReference 没有内建的类型选择界面，没有它策划无法在资产里新增多态元素。
    /// 可选类型为字段声明类型的全部非抽象、带无参构造、可序列化的子类。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReferencePickerAttribute : PropertyAttribute
    {
    }
}
