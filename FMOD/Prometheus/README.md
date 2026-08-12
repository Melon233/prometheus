# Prometheus FMOD 战斗音频工程

本工程由 FMOD Studio 2.03.13 创建，`Combat` Bank 保存项目当前全部战斗音效事件，Unity Integration 从 `Build` 目录读取并在运行时自动加载 `Master`、`Master.strings` 与 `Combat`。

## 事件映射

| FMOD 事件 | 源音频 | AnimationLine 与触发时间 |
| --- | --- | --- |
| `event:/Combat/Player/Normal_Attack_01` | `atk1.wav` | `atk1`、`atk1_move`：`0.00s` |
| `event:/Combat/Player/Normal_Attack_02` | `atk2.wav` | `atk2`、`atk2_move`：`0.15s` |
| `event:/Combat/Player/Normal_Attack_03` | `akt3.wav` | `atk3`：`0.10s` |
| `event:/Combat/Player/Normal_Attack_04` | `atk4.wav` | `atk4`、`atk4_move`：`0.45s` |
| `event:/Combat/Player/Special_Attack` | `atk1.wav` | `heavy`：`0.40s` |
| `event:/Combat/Player/Skill` | `较脆的爆炸－YS070523.wav` | `atk_branch`：`0.25s` |
| `event:/Combat/Player/Ultimate` | `击中－爆炸－碎裂mcx20070416.wav` | `xskill`：`2.0166667s` |
| `event:/Combat/Enemy/Slime_Attack` | `atk4.wav` | `skill_start`：`1.50s` |
| `event:/Combat/Shared/Hit_Flesh` | `击中-肉体3-ltt20070411.wav` | `leg_hitted`、`leg_hitted 1`：`0.00s` |
| `event:/Combat/Library/Sword_Whoosh` | `刀剑挥舞6-气流带摩擦-LTT20070510.wav` | 已导出备用，当前无 AnimationLine 绑定 |

## 重新构建

在 FMOD Studio 中打开 `Prometheus.fspro` 并执行 Build，或在项目根目录运行：

```powershell
& 'D:\FMOD Studio 2.03.13\fmodstudiocl.exe' -build -banks 'Master,Combat' -platforms 'Desktop' -export-guids 'FMOD\Prometheus\Prometheus.fspro'
```

Bank 构建完成后，在 Unity 执行 `Tools/Prometheus/Audio/Refresh Banks And Regenerate FMOD Event Enum`，以刷新事件缓存并重新生成稳定枚举与 GUID 映射。
