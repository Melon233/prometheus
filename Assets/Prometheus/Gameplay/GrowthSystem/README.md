# Prometheus 角色养成系统

本目录实现归属于每个 `PlayerEntity` 的角色等级、天赋等级、装备和武器养成。四条链路都遵循 `角色 Prefab 上的 Component 引用只读配置 SO -> Component 初始化当前 Entity 独占运行时副本 -> Logic 计算 -> 永久 Effect 投影 -> Property 或技能增益属性`，不会把可变运行态写入 Prefab、配置 `ScriptableObject` 或共享集合。

## 配置资产边界

- `CharaLevelConfig` 保存等级上限、最高等级攻击力、经验曲线和满级累计经验。
- `TalentConfig` 保存天赋成长参数以及角色普通攻击、特殊攻击、技能和大招的基础数值。
- `EquipmentConfig` 保存装备槽位、副词条槽位、等级上限、经验曲线、满级累计经验和词条档位预设。
- `EquipmentDefinition` 为每件可复用装备保存稳定编号与静态 `TierDefinition` 列表。
- `WeaponConfig` 保存当前角色武器的等级上限、经验曲线、满级累计经验、静态词条定义和词条档位预设。
- 角色 Prefab 的各 Component 只序列化对应 SO 引用与启动 Debug 数据；等级、经验、当前词条值、监听属性和 Effect 句柄全部属于运行时实例。

正式 Yefa 通过 `CharaLevelComponent.config`、四种技能 Component 的 `talentConfig`、`EquipmentComponent.config` 和 `WeaponComponent.config` 引用 `Assets/BundleResources/Config` 下的持久化 SO。这样同一套配置可以跨三个 Yefa Entity 复用，而每个 Entity 的运行态仍完全独立。

## 组合与生命周期

- `PlayerEntity` 从角色 Prefab 注册 `CharaLevelComponent`、`EquipmentComponent` 和 `WeaponComponent`，四种技能 Component 继续分别持有自己的天赋等级数据。
- `EffectLogic` 在 `Buff` 阶段先把 Entity 接入单局 `EffectSystem`；四种养成 Logic 在 `Gameplay` 阶段创建各自的永久 Effect。
- 每个永久 Effect Id 都包含 `EntityId`，格式为 `Growth.{EntityId}.{Channel}`，因此三个 Yefa 小队成员拥有完全独立的养成实例和 Modifier。
- 养成 Logic 使用 `LogicControlRequirement.None`，角色离场、眩晕、受击或死亡动作都不会暂停等级、经验和 Effect 生命周期。
- Entity 释放时先移除活动 Effect，由 `EffectInstance` 资源句柄精确回滚 Modifier，再释放临时运行时定义。

## 通用经验曲线约定

角色、装备和武器都把累计总经验约束到 `0..maximumExperience`，先计算 `normalizedExperience = totalExperience / maximumExperience`，再将 `AnimationCurve.Evaluate(normalizedExperience)` 约束到 `0..1`。Component 初始化时会从配置 SO 深拷贝曲线关键帧与 WrapMode，运行中不会共享或修改配置资产曲线。

曲线只控制等级阈值，最终等级向下取整：

- 角色和武器：`Floor(1 + curveProgress × (maximumLevel - 1))`，等级范围为 `1..maximumLevel`。
- 装备：`Floor(curveProgress × maximumLevel)`，等级范围为 `0..maximumLevel`。

之所以选择向下取整，是因为等级通常表示已经跨过的离散阈值；直接把浮点结果转为四舍五入会在阈值中点提前升级。词条也按当前离散等级进度成长，因此同一级内增加经验不会让战斗属性连续漂移。

## 角色等级

`CharaLevelComponent` 引用 `CharaLevelConfig` 并保存启动 Debug 累计经验，在 `CharaLevelLogic.AfterNew` 时复制等级上限、最高等级攻击力、经验曲线和满级累计经验，随后只读取当前 Entity 独占副本。

- 初始等级默认为 1，当前等级完全由累计总经验和曲线推导，不重复保存可独立修改的等级输入。
- `AddExperience(float)` 增加非负累计总经验，达到满级经验后拒绝溢出值并返回实际接受经验。
- 等级攻击力固定增益为 `最高等级攻击力 / (等级上限 - 1) × (当前等级 - 1)`；等级上限为 1 时安全返回 0。
- `CharaLevelLogic` 通过 `Growth.{EntityId}.CharaLevel` 永久 Effect 向 `PropertyType.Atk` 的 `Offset` 通道写入增益。
- `LevelProperty` 与 `TotalExperienceProperty` 可直接接入现有 `ListenSystem`；经验变化未跨级时不会重建攻击力 Effect。

正式 Yefa 当前配置为等级上限 90、满级经验 8900、线性曲线和最高等级攻击力 890，因此每 100 点累计经验提升一级，每级永久增加 10 点攻击力。

## 天赋等级

`TalentConfig` 统一配置天赋成长系数和最高等级。`AttackComponent`、`SpecialAttackComponent`、`SkillComponent`、`UltimateComponent` 各自序列化一个 `TalentGrowthState`，因此每个技能都有独立的 Debug 等级、当前等级和 `ModifiableProperty` 增益系数。

- 增益系数为 `(技能等级 - 1) × 天赋成长系数`。
- 技能基础倍率或基础增益值最终乘以 `1 + 增益系数`。
- `TalentLogic` 使用唯一的 `Growth.{EntityId}.Talent` 永久 Effect，并通过四个 `TalentGainModifierOperation` 分别修改四个技能 Component 的增益属性。
- 普通攻击、特殊攻击、技能和大招在创建命中上下文时读取各自 `TalentScale`；固定伤害偏移不参与倍率成长。
- `TrySetTalentLevel` 只接受 1 到配置上限内的等级，并在安全更新边界重建永久 Effect。

正式 Yefa 当前配置为最高 10 级、成长系数 0.1；例如 5 级技能的最终基础倍率缩放为 1.4。

## 词条配置与运行时数据

- `TierType` 只允许攻击力、防御力、暴击率、暴击伤害和生命值上限，并由 `TierRules` 集中映射到现有 `PropertyType`。
- `TierValuePreset` 的两个浮点值分别是该档位满级时的 `maximumOffset` 和 `maximumCoefficient`，二者可以同时非零；填零表示禁用对应通道。
- `TierDefinition` 只保存是否为主词条、属性类型和整数档位。
- `TierInstance` 保存自己的 `TierDefinition` 副本、当前系数和当前偏移；当前值等于档位最大值乘离散等级进度。
- `TierPreset` 与 `AnimationCurve` 都会在 Component 初始化时深拷贝，使每个 PlayerEntity 的运行时数据彼此独立。
- 属性汇总仍遵循现有公式 `BaseValue × (1 + Boost 总和) + Offset 总和`。

## 装备

`EquipmentComponent` 引用 `EquipmentConfig` 并保存启动 Debug 装备列表，在 `EquipmentLogic.AfterNew` 时复制槽位数、副词条槽位数、装备最高等级、经验曲线、满级累计经验和档位预设。

- `EquipmentDefinition` 是独立只读 SO，只包含稳定编号与 `TierDefinition` 列表，可被 Debug 配置、背包、掉落和多个角色复用。
- `EquipmentInstance` 持有只读 `EquipmentDefinition` 资产引用、从定义深拷贝得到的 `TierInstance` 列表、当前累计总经验和当前等级；可变数值不会回写 Definition。
- 每件装备初始等级为 0；档位预设值是满级最大值，所以 0 级词条两个通道均为 0。
- 每件装备必须恰好包含一个主词条，副词条数量不能超过 `subTierSlotCount`。
- `TryEquip(slot, definition, totalExperience)` 会先完整验证再创建运行时实例；`TryUnequip` 只清理指定槽位。
- `AddExperience(slot, value)` 刷新指定装备等级和词条实例；只有跨过整数等级时才要求 `EquipmentLogic` 重建属性 Effect。
- `EquipmentLogic` 按 `PropertyType + PropertyModifierMode` 汇总全部当前词条，并通过唯一的 `Growth.{EntityId}.Equipment.Tiers` 永久 Effect 投影。
- `RevisionProperty` 会在装备、经验、等级或词条值变化时通知 UI 与存档层。

正式 Yefa 当前配置为 5 个装备槽位、每件最多 4 个副词条、装备最高 20 级、满级经验 2000 和线性曲线；Debug 装备列表默认为空。

## 武器

`WeaponComponent` 引用 `WeaponConfig` 并保存启动 Debug 累计经验，在 `WeaponLogic.AfterNew` 时复制武器等级上限、经验曲线、满级累计经验、词条定义和档位预设。

- 武器成长参考角色，初始等级为 1，等级范围为 `1..maximumLevel`。
- Component 持有当前等级、累计总经验和 `TierInstance` 列表；等级、曲线、预设和词条实例均为当前 Entity 独占副本，运行时不会写入 `WeaponConfig`。
- `AddExperience(float)` 更新累计经验，跨过整数等级时刷新词条实例并通知 `WeaponLogic` 重建 Effect。
- 非空武器词条列表必须恰好包含一个主词条。
- `WeaponLogic` 汇总两个 Modifier 通道，并通过唯一的 `Growth.{EntityId}.Weapon.Tiers` 永久 Effect 修改角色属性。
- `LevelProperty` 与 `TotalExperienceProperty` 可供 UI 和存档监听。

正式 Yefa 当前配置为武器最高 90 级、满级经验 8900、线性曲线，并放置一条攻击力一档主词条。一级时词条为零，满级时同时达到 10 点固定攻击力和 5% 攻击力系数。

## Debug 配置

- `CharaLevelComponent.debugData.totalExperience`：启动角色累计总经验。
- 四种技能 Component 的 `talentGrowth.debugTalentLevel`：启动技能等级。
- `EquipmentComponent.debugEquipment`：按列表下标配置 `EquipmentDefinition + totalExperience` 并应用到装备槽位。
- `WeaponComponent.debugData.totalExperience`：启动武器累计总经验。

Debug 数据刻意保留在角色 Prefab 的 Component 中，因为它表达某个 Entity 实例启动时采用的测试状态，不属于可跨角色共享的静态系统配置。正式存档接入后可由存档数据覆盖这些启动副本，而不需要修改任何配置 SO。

## 尚可优化或需要产品确认的点

- 当前把档位二元组解释为 `(maximumOffset, maximumCoefficient)`，并允许两个通道同时生效。如果产品语义是“二选一”，应在 `TierDefinition` 增加模式枚举，而不是依赖某个值填零。
- 当前角色和武器初始等级为 1，装备初始等级为 0。新版 Spec 没有再次明确武器初始等级；实现沿用“参考角色成长逻辑”的含义。
- 当前主词条与副词条使用同一套档位预设。如果主词条需要更高数值档位，建议把预设键扩展为 `TierType + IsMainTier`，避免为主副词条人为分配不直观的档位编号。
- 当前曲线输出被约束到 `0..1`。这能阻止切线过冲造成越级，但也意味着设计者不能故意配置超出范围的奖励区间。
- 当前 `WeaponConfig` 同时承担武器 Definition 与成长配置，适合角色固定一把武器的现阶段需求。如果后续允许换武器，建议新增独立 `WeaponDefinition` SO，并让 `WeaponComponent` 的运行时实例引用它，边界与现有 `EquipmentDefinition` 一致。

## 验证

`Prometheus.Growth.EditorTests` 使用正式 Yefa Prefab、正式配置 SO 和正式 EffectLibrary 验证 Prefab 配置引用、PlayerEntity 组合、曲线等级映射、角色升级攻击力、天赋倍率、0 级到满级装备词条、1 级到满级武器词条、固定值与系数同时汇总，以及内部成长 Effect 不进入 HUD Buff 列表。
