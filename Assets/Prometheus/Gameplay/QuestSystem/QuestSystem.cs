// using System.Collections.Generic;
// using NUnit.Framework.Constraints;
// using Xuan.Prometheus;

// namespace Xuan
// {
//     public class QuestConfig
//     {
//         string id;
//         string name;
//         string desc;
//         QuestType questType;
//         List<Condition> conditions;
//         List<Reward> rewards;
//         List<QuestNode> questNodes;
//     }

//     internal class Reward
//     {
//     }

//     public class Condition
//     {

//     }
//     public class QuestNode
//     {
//         public string id;
//         public string desc;
//         public List<Objective> objectives;
//         public List<GameAction> onEnter;
//         public List<GameAction> onComplete;
//     }

//     public class GameAction
//     {
//         public IEvent evt;
//         public List<object> parameters;
//     }

//     public class Objective
//     {
//         public ObjectiveType objectiveType;
//         public int targetId;
//         public int requiredConunt;
//     }

//     public enum ObjectiveType
//     {
//         Talk,
//         Reach,
//         Kill,
//         Collect,
//         FinishCutscene
//     }

//     public enum QuestType
//     {
//         Main
//     }
//     public class Quest
//     {
//         string id;
//         QuestState questState;
//         int curNodeIdx;
//         public int[] objectivesProgress;
//     }

//     internal enum QuestState
//     {
//         Idle,
//         Excuting,
//         Completed
//     }

//     public interface IQuestSystem
//     {

//     }
//     public class QuestSystem
//     {

//     }
// }