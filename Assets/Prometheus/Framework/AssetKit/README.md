# AssetKit 与 YooAsset 地址规范

## 当前配置

项目使用 YooAsset 的 `DefaultPackage` Package，并通过 `Assets/BundleCollectorSetting.asset` 收集 `Assets/BundleResources` 下的运行时主资产。Player Build 只直接进入 `Assets/Resources/Entry.unity`，正式玩法场景由 GameplayKit 通过 AssetKit 加载。

所有启用寻址的 Collector 统一使用 `AddressByFileName`。资源 Address 等于不带扩展名的文件名，例如 `Yefa.prefab` 的 Address 为 `Yefa`，`FloatingTextConfig.asset` 的 Address 为 `FloatingTextConfig`。

## 命名约束

- 同一个 YooAsset Package 内，所有被收集为主资产的文件名必须唯一，比较时忽略扩展名并按不区分大小写处理。
- 目录不参与 Address，因此资源在 `Assets/BundleResources` 内调整目录时不会改变运行时 Address。
- 文件改名会改变 Address，属于加载接口变更；改名前必须同步修改代码常量、Prefab、Scene 和配置中的序列化地址。
- 只有需要通过 AssetKit 直接加载的主资产才需要 Address，材质、贴图和动画等依赖资源不应为了寻址而改名。
- YooAsset 收集和构建阶段会拒绝同一 Package 内的重复 Address；新增 Collector 时也必须继续使用文件名唯一规则。

## 当前运行时地址

| 资源类型 | 文件示例 | Address 示例 |
| --- | --- | --- |
| 角色 Prefab | `Character/Yefa.prefab` | `Yefa` |
| 敌人 Prefab | `Enemy/Slime.prefab` | `Slime` |
| UI Prefab | `UI/Hud/Prefabs/HudPanel.prefab` | `HudPanel` |
| 全局配置 | `Config/Global/FloatingTextConfig.asset` | `FloatingTextConfig` |
| 效果配置入口 | `Config/Effect/EffectLibrary.asset` | `EffectLibrary` |
| 玩法场景 | `Scene/SampleScene.unity` | `SampleScene` |

## 旧地址迁移

| 旧 Address | 新 Address |
| --- | --- |
| `Character_Yefa` | `Yefa` |
| `Character_Yousaer` | `Yousaer` |
| `Character_Senyin` | `Senyin` |
| `Enemy_Slime` | `Slime` |
| `Prefabs_HudPanel` | `HudPanel` |
| `Prefabs_Dmg` | `Dmg` |
| `Prefabs_WorldHpBar` | `WorldHpBar` |
| `Global_FloatingTextConfig` | `FloatingTextConfig` |

UI 代码生成器生成的 `UIPanelConfigAttribute` 也直接使用 Panel 文件名，确保新面板遵循同一规则。

## 配置资产放置规则

| 配置 | 用途 | 固定位置 | 处理原则 |
| --- | --- | --- | --- |
| `BundleCollectorSetting.asset` | YooAsset 编辑器收集与构建配置 | `Assets/BundleCollectorSetting.asset` | 由 YooAsset 通过 `AssetDatabase` 查找，不放入 `Resources`，也不作为运行时资产收集。 |
| `FloatingTextConfig.asset` | 伤害飘字运行时配置 | `Assets/BundleResources/Config/Global/FloatingTextConfig.asset` | 由 AssetKit 使用地址 `FloatingTextConfig` 加载，必须保留在 YooAsset 收集目录。 |
| `DOTweenSettings.asset` | DOTween 运行时全局配置 | `Assets/Resources/DOTweenSettings.asset` | 当前存储模式为 Resources，必须保留在 `Resources` 目录供 DOTween 自动初始化读取。 |
| `VLBConfigOverride.asset` | Volumetric Light Beam 运行时全局配置 | `Assets/Resources/VLBConfigOverride.asset` | 插件通过 `Resources.Load` 固定加载，必须保留在 `Resources` 目录。 |
| `SpineSettings.asset` | Spine 编辑器导入与偏好配置 | `Assets/Editor/SpineSettings.asset` | Spine 编辑器代码使用固定路径读取，不进入 Player，也不由 YooAsset 收集。 |
