# Trd 第三方资源目录

`Assets/Trd` 统一存放从外部供应商、Unity Asset Store 或开源项目导入的第三方代码、工具和美术资源。

## 特殊目录约束

- `Assets/Art` 是项目美术特殊目录，即使包含外部素材也永远保留在 `Assets` 根目录。
- `Assets/Plugins` 是 Unity 插件特殊目录，即使内容均为第三方插件也永远保留在 `Assets` 根目录。
- `Assets/Prometheus`、`Assets/BundleResources`、`Assets/Editor` 和 `Assets/Resources` 存放项目自建内容，不归入 `Trd`。

## 迁移规则

- 已被 Git 跟踪的目录和对应 `.meta` 文件必须使用 `git mv` 迁移，确保 Git 将变更识别为重命名。
- 未被 Git 跟踪的资源无法使用 `git mv`，移动后保持未跟踪状态，是否纳入版本库由提交者决定。
- 迁移时必须保留原有 `.meta` 文件及 GUID，并同步修改 `AssetDatabase`、安装器和配置文件中的硬编码资源路径。
- 新增第三方资源默认放入本目录；只有依赖 Unity 特殊目录语义的插件才放入 `Assets/Plugins`。

## 当前内容

当前目录包含音频裁剪工具、场景与特效素材、MF.SSGI、PostProcessing、ShinySSRR、Spine、SuperScrollView、TextMesh Pro、UniStorm、Volumetric Light Beam 等第三方资源。

## nTools 迁移说明

PrefabPainter 的工作目录必须根据 `Assets/Trd/nTools/PrefabPainter/Scripts/Editor/PrefabPainter.cs` 的实时资源路径计算，不得缓存迁移前的目录。用户设置统一保存在 `Assets/Trd/nTools/PrefabPainter/Settings/settings.asset`，根目录 `Assets/nTools` 不再使用。
