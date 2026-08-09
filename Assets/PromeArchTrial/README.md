# PromeArchTrial 角色联机 Demo

本目录实现客户端预测、服务器权威和纯表现驱动的 30 Hz 角色战斗纵切。角色共享逻辑是纯 C#，Unity 只采集输入、连接网络并播放 Spine 表现；服务端和客户端从同一份 Luban Excel 数据生成各自投影，不读取 `ScriptableObject`，也不存在 `rootId` 根配置。

## 分层与依赖

- `Core/CoreNative`：TCP 四字节长度帧、Protobuf v5 协议、Luban cs-bin 运行时和双端共享基础类型。
- `Core/CoreUnity`：Unity 侧后台 TCP 适配器；后台线程不访问 Unity API。
- `Game/GameNative/Character`：不可变配置、完整角色状态、固定点数模拟、动作事件和预测恢复重放。
- `Game/GameNative/World`：服务器唯一权威世界、统一 Tick、输入超时、提交结果分类、两阶段命中与确定性伤害结算。
- `Game/ConfigAdapter`：通过 Luban 自动解析的 `*_Ref` 将 Character、属性、移动、闪避和 Action 表编译为纯 C# `CharacterRuntimeConfig`。
- `Game/GameUnity`：输入锁存、30 Hz 命令上传、完整状态对账、未确认命令重放、Ping 与权威战斗事件桥接。
- `Presentation/Character`：只消费表现快照并在 Spine track 0 播放动画，同时更新血条和伤害飘字；不会反向修改模拟状态。
- `Bootstrap`：加载角色 `1001` 的 Luban 客户端数据，实例化净化后的 Yefa 表现 prefab，并组装会话和表现桥。

依赖方向始终指向纯 C# 核心；表现程序集不能被 gameplay 或服务器依赖。旧 `Assets/Prometheus` 中的 MonoBehaviour、碰撞盒动画事件和角色根对象状态不参与新模拟。

## 配置生成

唯一策划工作簿是 `Configs/Xlsx/CharacterConfig.xlsx`，表结构由 `Configs/Luban/Defines/gameplay.xml` 定义。共享 gameplay 字段使用 `c,s` 组，Spine 动画名和 prefab 路径只使用 `c` 组；所有表关系都声明为 Luban `#ref`，生成代码同时保留原始 ID 和已解析的 `*_Ref`。

```bat
Configs\Luban\validate.bat
Configs\Luban\generate-client.bat
D:\Unity Projects\battle-server\generate-config.bat
```

客户端二进制数据输出到 `Assets/StreamingAssets/PromeArchTrial/Config`，服务端二进制数据输出到 `D:\Unity Projects\battle-server\ConfigData\Luban`。修改 Excel 或 schema 后必须重新生成双端数据并确认两端 `CharacterRuntimeConfig.ContentHash` 一致。

## 运行与操作

1. 运行 `D:\Unity Projects\battle-server\run-server.bat`。
2. 打开 `Assets/PromeArchTrial/Scenes/PromeArchTrialClientDemo.unity`；需要重建场景时执行 `Tools/PromeArchTrial/Create Client Demo Scene`。
3. 进入 Play Mode，等待 Debug 面板显示连接成功。

| 输入 | 行为 |
| --- | --- |
| `WASD` | XZ 平面八方向移动 |
| `Ctrl + WASD` | 行走 |
| `WASD` | 跑步 |
| `Shift + WASD` | 冲刺 |
| `Space` | 跳跃、上升、下落与落地 |
| 鼠标右键 | 向前闪避；按住 `S` 时向后闪避 |
| 鼠标左键短按 | 四段普通攻击连击 |
| 鼠标左键长按 | 重击 |
| `E` | 技能 |
| `R` | 终结技 |

Debug 面板显示连接状态、客户端/服务器 Tick、确认 Tick、完整状态修正次数、位置阈值回滚次数、预测误差和独立 Ping/Pong RTT。Welcome 后客户端先上传四条 neutral 命令，并持续让预测时间线领先权威时间线四个 Tick；实际按键仍在下一个本地预测 Tick 立即响应，而正常网络延迟不会让每条输入都迟到。收到服务器快照时，客户端恢复完整权威状态，并按 Tick 顺序重放所有尚未确认的本地命令。

## 协议与权威边界

协议版本为 v5。每个 TCP 负载都是一个生成的 `BattleEnvelope` Protobuf 消息，外层保留四字节小端长度前缀。握手只发送协议版本和共享配置内容哈希；Welcome 返回玩家/实体/角色编号、服务器 Tick 和完整初始角色状态。输入消息携带完整 `CharacterCommand` 及该 Tick 的预测状态，快照携带完整权威状态和与该角色相关的权威战斗事件。服务器可以覆盖尚未发送的旧状态，但会按 `worldTick + ordinal` 有界去重并可靠保留事件，只有 TCP 写成功后才移除；late/duplicate 输入按幂等诊断处理，过远 future 输入仍是协议错误。

权威 schema 位于 `Core/CoreNative/Networking/Protobuf/BattleMessages.proto`，生成文件位于相邻 `Generated` 目录。修改 schema 后运行服务端 `generate-protobuf.bat` 并同时提交 schema 与生成代码；已删除字段的 tag 必须保留为 `reserved`，禁止复用。

## 验证

- `PromeArchTrial.Character.EditorTests`：固定点角色行为、轻击缓冲、一次按住一次重击、移动攻击变体、真实恢复重放、Protobuf v5 往返和非法输入校验。
- `AuthoritativeBattleWorldBehaviorProbe`：全局 Tick、缺包连续量、输入边界、同 Tick 命中、暴击/防御、事件顺序和重复运行哈希。
- 服务端 `run-smoke-test.bat`：Luban ref、配置哈希、全部角色动作、双实体命中、Welcome/Pong/快照与本地 30 Hz 网络节拍。
- 最终表现验收必须在 Play Mode 检查 Yefa 动画、移动、跳跃、闪避、攻击、技能、受击、血条和伤害飘字；仅有编译或零项测试结果不等于通过表现验收。独立场景 `Presentation/Character/Scenes/YefaCharacterPresentationAcceptance.unity` 可在没有服务器时验证全部表现快捷键。
