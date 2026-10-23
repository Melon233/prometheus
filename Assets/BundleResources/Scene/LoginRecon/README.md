# Clean.unity — 登录场景重建（仅使用截帧资产）

从 RenderDoc 捕获的 Vulkan 帧还原「云海石柱长廊」登录场景的视觉。
**本场景不引用任何项目美术资源**：无 Polyart 预制体、无 Fantasy Skybox、无第三方材质/贴图。
几何体全部由代码生成（blockout），贴图全部导出自 `.rdc`，天空与环境色取自截帧像素采样。

## 源数据

| 项 | 值 |
|---|---|
| 截帧 | `%TEMP%\RenderDoc\MuMuNxDevice_2026.10.21_11.16_frame13516.rdc` |
| 帧号 / 大小 | #13516 / 100.5 MB（capture section 237 MB） |
| API | Vulkan，NVIDIA RTX 3060（581.4） |
| 贴图导出工具 | `C:\UGit\d3d11-wrapper\tools\rdc-probe\export_textures.py`（`qrenderdoc --python` 无头执行，34 张） |

## 贴图来源（全部在 `FromCapture/Tex/`）

| 材质 | 贴图 | 用途 |
|---|---|---|
| `M_Cap_ColumnCarved_Tiled` | `tex_65747_1024x1024.png`（雕花石材 albedo）<br>`tex_65776_1024x1024.png`（对应法线） | 柱身/柱头/柱础、路缘、栏杆柱 |
| `M_Cap_Brick_Tiled` | `tex_66021_1024x1024.png`（石砖 albedo）<br>`tex_65909_1024x1024.png`（对应法线） | 平台、栏板、栏杆扶手、远景体块 |
| `M_Cap_Stone_Tiled` | `tex_65974_1024x1024.png`（石板 albedo） | 中央步道 |
| `M_SkyRecon` | 无贴图（Unity Procedural Skybox） | 天顶 `#284485`、地平线 `#def9ff`，由截帧像素采样标定 |

其余导出物（最终帧 `tex_1217`、法线/掩码/交换链 RT）仅用于比对，未参与渲染。

## 场景结构（`LoginRecon`）

```
LoginRecon
├─ Geometry
│  ├─ Terrace / Walkway / CurbL,R / ParapetL,R / Fascia   （程序化 Cube + 截帧贴图平铺）
│  ├─ Colonnades
│  │  ├─ ColumnR_0..6  x=+8.4, z=2..67
│  │  └─ ColumnL_0..5  x=-8.6, z=7..65
│  │       每根：Base(4.0×0.7) → Plinth(3.4×0.45) → Shaft(Ø2.7×14) → CapA → CapB → Abacus
│  ├─ Balustrade（40 根栏杆柱 + 扶手，截帧石材）
│  └─ DistantCity（16 个程序化体块，z=420..740，靠雾隐没）
├─ Environment/Sun（Directional 48°/-40°，暖白 1.18，Soft Shadow 0.82）
├─ LoginCam（FOV 55，位置 (-0.6, 2.35, -9)，俯仰 2°，偏航 -3°）
└─ PostVolume（Global：Bloom 0.42 / ColorAdjustments exposure +0.32、contrast +2、saturation -6 / Vignette 0.14）
```

渲染：URP Mobile Forward（`PrometheusMobileForwardMidPipeline`），线性空间，HDR 关闭；
Fog ExponentialSquared，density `0.0088`，color `#E6F5FF`；Ambient = Skybox，强度 `0.95`。

## 与截帧的对应 / 近似说明

| 截帧特征 | 重建方式 |
|---|---|
| 两侧白石柱廊向消失点收束 | 程序化柱列（位置/间距按画面透视标定）+ 55° 相机 |
| 暖白雕花石材表面 | 截帧 albedo + normal（`tex_65747` / `tex_65776`） |
| 石板地面、砖石平台 | 截帧 `tex_65974` / `tex_66021` |
| 天顶深蓝 → 地平线泛白 | Procedural Sky 按采样色标定 + 指数雾 |
| 远景白色建筑隐入雾中 | 程序化体块 + 雾 |
| 云海 / 中景云层 | **未重建**：截帧未导出可复用的云/天空盒贴图，当前以雾+天空渐变近似 |
| 登录 UI（12+、FPS、水印） | 未重建（UI 层，不属于 3D 场景内容） |

## 为什么是 blockout

截帧里能拿到的是**渲染期资源**（纹理、RT、管线状态），不是美术工程：
网格顶点虽在 RDC 里，但要做「原样重建」还需按 view-proj 还原世界变换与实例化关系。
当前做法是把可复用的**表面贴图**取出来，几何按画面透视手工搭到同构图，
因此柱子是「圆柱+圆盘柱头」的简化柱式，而非原始高模。
