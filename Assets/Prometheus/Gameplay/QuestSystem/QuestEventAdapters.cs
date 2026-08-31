namespace Xuan.Prometheus.Quest
{
    /// <summary>提供常用外部领域事件的统一转换入口，避免各系统拼接 EventId 规则不一致。</summary>
    public static class QuestEventAdapters
    {
        /// <summary>发布 Film 完成事件。</summary>
        public static void PublishFilmCompleted(QuestSystem questSystem, string eventId, string filmId) { questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.FilmCompleted, filmId)); }
        /// <summary>发布对话完成事件。</summary>
        public static void PublishDialogueCompleted(QuestSystem questSystem, string eventId, string dialogueId, string result = null) { questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.DialogueCompleted, dialogueId, 1, result)); }
        /// <summary>发布物品获得事件。</summary>
        public static void PublishItemAdded(QuestSystem questSystem, string eventId, string itemId, int amount) { questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.ItemAdded, itemId, amount)); }
        /// <summary>发布敌人击败事件。</summary>
        public static void PublishEnemyDefeated(QuestSystem questSystem, string eventId, string enemyId, int amount = 1) { questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.EnemyDefeated, enemyId, amount)); }
        /// <summary>发布进入区域事件。</summary>
        public static void PublishEnteredRegion(QuestSystem questSystem, string eventId, string regionId) { questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.EnteredRegion, regionId)); }
    }
}
