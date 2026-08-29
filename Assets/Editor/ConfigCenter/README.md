# ConfigKit 配置中心

ConfigKit 是 Editor-only 的 ScriptableObject 配置导航工具，不改变任何运行时配置读取链路。入口为 `Prometheus/Config Center`。

## 扫描范围

默认扫描 `Assets/Prometheus`、`Assets/BundleResources/Config` 和 `Assets/Config`，排除 `Plugins`、`Trd`、`ThirdParty`、`Tests` 和 `Test` 路径。索引保存于 `Library/Prometheus/ConfigCenter/config-index.json`，属于可重建的编辑器派生数据。

## 分组规则

“全部配置”下默认以 `Assets/BundleResources/Config` 和 `Assets/` 作为一级目录。点击搜索栏中的“配置”Toggle 后，可在搜索栏上方编辑一级目录列表；目录设置保存在 `EditorPrefs`，修改、添加或删除目录后会自动重建索引。多个目录发生重合时，配置归属按最长匹配路径确定，更深的目录优先收集配置。配置类型可以使用 `ConfigCenterGroupAttribute` 指定各扫描根目录内部的分组，使用 `ConfigCenterDisplayNameAttribute` 指定显示名；没有显式声明时，ConfigKit 按资产相对于所属扫描根目录的目录结构推导分组，目录无法提供有效层级时退化为 C# 类型名。

## 窗口功能

Config Center 自身只显示一棵完整配置目录树，不再显示独立配置栏，也不在窗口内部嵌入 Inspector。目录以“全部配置”为根节点，先显示当前配置的一级扫描根目录，再根据各根目录内部的分组路径构建文件夹层级，并把配置资产作为对应文件夹下的叶子节点显示。全部配置根节点始终展开，其他文件夹首次打开时默认折叠；所有文件夹都显示箭头，鼠标左键按下箭头或文件夹文字都会立即展开或折叠，箭头与文字保持垂直居中。用户手动操作的折叠状态使用 EditorPrefs 持久化。搜索框输入内容时，目录树会递归隐藏不包含匹配配置的文件夹，只显示匹配配置及其父级目录；点击匹配配置后自动清空搜索、展开“扫描根目录 + 分组路径”的完整父级链路并滚动到该配置。选中配置后会同步 `Selection.activeObject` 并打开或激活 Unity 原生 `InspectorWindow`，因此 Inspector 可以像通过“右键 Config Center Tab -> Add Tab -> Inspector”一样由 Unity 独立停靠、拖拽和分窗。具体停靠位置由当前 Editor 布局和用户操作决定，ConfigKit 不接管 Unity 的窗口布局。

顶部搜索栏仅保留“刷新”操作，搜索栏下方不再显示额外的“配置目录”标题；文件夹名称使用淡蓝色文字且保持透明背景，选中项使用绿色背景。支持名称、类型和路径搜索，点击配置条目即可选中资产并同步到独立 Inspector。资产导入、删除或移动后会合并刷新请求，并在导入批次结束后执行一次完整索引重建。

## 使用约定

配置中心只负责定位和编辑真实资产，不复制配置、不缓存字段值、不承担运行时加载。第三方配置默认不进入列表；需要扩展扫描范围时应修改 `ConfigCenterIndexer` 的根目录白名单，并同步评估索引噪声。

## Project 窗口导航历史

全局导航脚本位于 `Assets/Editor/ProjectNavigationHistory.cs`，与 Config Center 功能独立。它记录 Project 窗口中选中的 `Assets` 资产和文件夹路径。鼠标侧键 XButton1（Unity IMGUI 的 button `3`）执行后退，XButton2（button `4`）执行前进；回放导航不会重复写入历史。没有侧键时可以使用 `Prometheus/Navigation/Back` 和 `Prometheus/Navigation/Forward` 菜单命令。新选择会清空前进历史，行为与浏览器一致。
