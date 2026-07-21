// Assets/Editor/SpineEventScanner.cs
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

public static class SpineEventScanner
{
    [MenuItem("Tools/Spine/Print Events In Selected Skeleton")]
    static void PrintEvents()
    {
        var asset = Selection.activeObject as SkeletonDataAsset;
        if (asset == null)
        {
            Debug.LogWarning("请先在 Project 窗口选中 SkeletonDataAsset。");
            return;
        }

        var skeletonData = asset.GetSkeletonData(true);

        foreach (var animation in skeletonData.Animations)
        {
            var timelines = animation.Timelines;

            for (int i = 0; i < timelines.Count; i++)
            {
                if (!(timelines.Items[i] is EventTimeline eventTimeline))
                    continue;

                var times = eventTimeline.Frames;
                var events = eventTimeline.Events;

                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    var spineEvent = events[eventIndex];

                    Debug.Log(
                        $"[Spine Event] 动画={animation.Name} | " +
                        $"时间={times[eventIndex]:F3}s / {animation.Duration:F3}s | " +
                        $"事件={spineEvent.Data.Name} | " +
                        $"字符串参数={spineEvent.String}");
                }
            }
        }
    }

    [MenuItem("Tools/Spine/Print Events In Selected Skeleton", true)]
    static bool ValidatePrintEvents()
    {
        return Selection.activeObject is SkeletonDataAsset;
    }
}