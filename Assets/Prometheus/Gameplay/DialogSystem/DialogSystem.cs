// namespace Xuan
// {
//     public class DialogConfig
//     {
//         public string id;
//         public List<Statement> statements;
//     }
//     public class Statement
//     {
//         public string id;
//         public string content;
//         public bool hasOptions;
//         public List<Option> options;
//     }
//     public class Option
//     {
//         string id;
//         string content;
//         int offset;
//     }
//     public interface IDialogSystem
//     {
//         void PlayDialog(string dialogId);
//         void Select(string optionId);
//     }
//     public class DialogSystem : XSystem
//     {
//     }
// }