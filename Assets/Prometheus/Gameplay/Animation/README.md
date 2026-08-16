# Prometheus 动画系统

## 职责边界

- `AnimationSemantic` 是播放 API 的稳定语义标识，Logic 不再持有或传递具体动画资源。
- `AnimationLine` 是唯一动画资源入口，负责声明动画语义、包装 Spine 动画并合并 Unity 侧事件标记。
- `AnimationLibrary` 是角色共享的纯配置，并为该角色建立 `AnimationSemantic -> AnimationLine` 唯一索引；它不保存 Entity、Component、Logic 或 TrackEntry。
- `SpineComponent` 是唯一播放仲裁组件，负责优先级、主轨所有权、序列播放和一次性会话。
- `AnimationPlayback` 表示一次播放会话，分别转发普通 Spine Event、AnimationLine 强类型玩法命令和唯一结束原因。
- `Logic` 负责输入、状态切换、命中盒、音效、特效、伤害信号和其他玩法行为。
- `Component` 只保存 Unity 引用与运行数据，不包含玩法流程。

## 主轨优先级

| 优先级 | 数值 | 用途 |
| --- | ---: | --- |
| Idle | 0 | 待机循环 |
| Landing | 100 | 落地动画 |
| Locomotion | 200 | 行走、跑步、冲刺 |
| Airborne | 300 | 起跳、上升、下落 |
| Attack | 400 | 普通攻击 |
| SpecialAttack | 450 | 特殊攻击 |
| Dodge | 500 | 闪避 |
| Skill | 600 | 主动技能 |
| Ultimate | 700 | 终结技 |
| HitReaction | 800 | 受击 |
| Death | 1000 | 死亡 |

新请求的优先级低于当前会话时会被拒绝；等优先级允许同类状态切换；更高优先级会抢占当前会话。循环动画必须由对应所有者主动停止，Logic 只能停止自己启动的动画。

落地示例：`IdleLogic` 在无输入时持续请求 `Idle`，但不能打断 `Landing`；出现移动输入后，`GroundMoveLogic` 请求 `Locomotion`，因此立即打断落地动画。

## 会话生命周期

`SpineComponent.TryPlay` 和 `TryPlaySequence` 只接受 `AnimationSemantic`，由当前角色的 `AnimationLibrary` 解析实际 `AnimationLine`。成功时返回 `AnimationPlayback`，语义缺失、配置冲突或优先级不足时返回空。Logic 根据用途订阅该会话的 `EventReceived`、`CommandReceived` 与 `Finished`，并在 `Completed`、`Interrupted`、`Stopped`、`Disposed` 任一路径执行对称清理。禁止直接订阅裸 `TrackEntry`，也禁止绕过 `SpineComponent` 调用 `SkeletonAnimation.AnimationState.SetAnimation`。

```csharp
AnimationPlayback playback = spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true);
```

相同 Logic 可以对角色和史莱姆请求同一个 `AnimationSemantic.Idle`；最终分别解析为各自动画库中的待机动画。需要新增玩法语义时，应先扩展 `AnimationSemantic`，再为每个支持该动作的角色配置对应 `AnimationLine`。同一个动画库内不允许两个不同 `AnimationLine` 使用同一语义。

受击配置允许只设置 `attackedLine`，也允许同时设置 `nextAttackedLine`。存在恢复段时，`AttackedExecutor` 会自动播放 `Hit -> HitRecovery` 序列，不需要额外布尔开关；连续受击会重启整个序列，但只在最后一个恢复段完成后发布一次受击结束事件。

## 玩家战斗动作

- `NormalAttackLogic`、`SpecialAttackLogic`、`SkillLogic` 和 `UltimateLogic` 分别消费自己的输入并持有独立 `AnimationOwner`；`PlayerCombatActionLogic` 只复用强类型时间轴命令、命中发布和控制锁的公共机制。
- `TalentLogic` 不再处理任何具体动作，只初始化战斗心流被动，并通过一个永久 Effect 把四个独立技能等级映射为各自 Component 的增益系数。
- 四个玩家能力组件引用同一个角色 `TalentConfig`。其中集中保存普通攻击连段间隔及逐段倍率、偏移、标签，特殊攻击蓄力时间，特殊攻击、技能和大招的倍率、偏移、动画速度，以及统一天赋成长系数和等级上限；每个能力 Component 自己持有 Debug 等级、当前等级和可修改增益系数。
- `AttackComponent.attackHits` 与 `AnimationLibrary.atkExecutor.attacks` 按下标一一对应，组件的每段 Inspector 配置只保留 `ColliderProxy` 和 `abilityId`；数值由 `TalentConfig.normalAttack.stages` 的同一下标提供，命中伤害按 `Max(0, GetCalculatedDamage × damageMultiplier × TalentScale + damageOffset)` 生成，其他三种能力使用相同的天赋倍率顺序。
- `ColliderProxy` 会把自身连同目标 Collider 一起传给处理器；Logic 只接受当前动画段的活动碰撞体，避免上一段迟到的物理回调被下一段命中信息接收。
- 每个实际攻击 `AnimationLine` 必须独立配置一对 `EnableHitbox/DisableHitbox` 命令；`AnimationLibrary` 不保存共享事件名，Logic 也不比较 `hit_start/hit_end` 等 Spine 字符串。动画被打断或异常结束时仍会执行兜底关闭，但正常命中窗口完全由当前 AnimationLine 配置决定。

## 敌人空中运动

敌人和角色共享 `MotionComponent` 与 `MotionLogic`。AI Logic 只写入水平速度，`EnemyAirMoveLogic` 根据 `PropertyComponent.Gravity` 更新竖直速度，`MotionLogic` 在 `AfterGameplay` 阶段把两者合成为一次 `CharacterController.Move`。受击、眩晕和死亡可以停止 AI 水平移动，但不会暂停重力。

`EnemyStunIdleLogic` 不依赖敌人的行动权限。Stun 存续期间它会持续清除水平速度并请求最低优先级 Idle；该请求不会打断受击或死亡动画，但会在受击会话完成并释放主轨后自动接管，避免敌人停在恢复动画末帧。

## 新增动画

1. 创建或选择一个 `AnimationLine`，设置唯一的 `Animation Semantic` 和 Spine `AnimationReferenceAsset`。
2. 在 `AnimationLine` Inspector 时间轴添加需要的普通事件或强类型 Gameplay Command；会产生攻击判定的动画必须配置 `EnableHitbox/DisableHitbox`。
3. 把 `AnimationLine` 配置到对应 `AnimationLibrary` 动作配置中，确保同一个库内没有语义冲突。
4. 由相应 Logic 选择语义、所有者与优先级并调用 `SpineComponent.TryPlay`。
5. 在 Logic 中处理事件和结束清理，不向 `AnimationLibrary` 或配置对象写入运行态。

语义迁移入口为 `Tools/Prometheus/Animation/Migrate Libraries To Semantic AnimationLine`。迁移会按照每个动画库字段的玩法职责为正式 `AnimationLine` 写入语义，并报告未配置语义或跨动画库复用时产生的语义冲突；该操作可以安全地重复执行。

需要从 Spine 动画引用批量创建新资产时，在 Project 窗口中选择同一目录下的一个或多个 `AnimationReferenceAsset`，右键执行 `Prometheus/Convert to AnimationLine`。工具会在源动画目录下创建唯一的 `AnimationLines` 子目录，并按源资产文件名生成对应 `AnimationLine`；选择中混入其他资源、跨目录选择、源动画失效或任一目标路径已存在时整批取消，不覆盖已有资产，也不生成带编号副本。生成后仍需为每个 `AnimationLine` 配置唯一语义和必要的时间轴事件，再接入对应 `AnimationLibrary`。
