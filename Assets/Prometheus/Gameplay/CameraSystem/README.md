# CameraSystem

## 目标

`CameraSystem` 负责当前单局唯一的输出相机与 Cinemachine 跟随镜头。角色 Prefab 只保留角色表现和玩法组件，不再携带 `Camera`、`AudioListener`、FMOD Listener、URP 相机数据或 SSGI 相机组件。

第一阶段只实现与旧角色子相机一致的坐标跟随：相机保持角色局部坐标 `(0, 3.4884024, -4.0738688)` 和原有局部旋转，不增加位置阻尼、旋转阻尼或切换混合。

## 运行链路

1. `GameplayKit.Configure` 注册 `CameraSystem`，并把 `GameplayStartupOptions.RuntimeRoot` 作为相机运行时根节点传入。
2. `CameraSystem.AfterNew` 创建输出 `Main Camera`、`CinemachineBrain`、`CinemachineCamera`、`CinemachineFollow` 和系统持有的 `Camera Follow Target`。
3. `TeamSystem.InitializeMembers` 默认激活第一个成员并发布 `ActiveTeamMemberChangedEvent`。
4. `CameraSystem` 根据事件中的 `CurrentEntityId` 查询角色场景对象，把 `Camera Follow Target` 挂到角色根节点并恢复旧相机的局部旋转。
5. `CinemachineFollow` 使用零阻尼 `LockToTarget` 坐标绑定，`CinemachineRotateWithFollowTarget` 同步角色朝向；角色切换时只迁移目标，不创建第二台输出相机。
6. `CameraSystem.Dispose` 解除事件并销毁全部相机运行时对象。

## 画面与监听配置

输出相机继续使用旧 Prefab 的透视参数与裁剪面，但不再写死 HDR、MSAA、Renderer Index、后处理和 Dithering。相机通过 Renderer Index `-1` 继承当前平台管线的唯一默认 Renderer，并订阅 `PrometheusRenderQualityController.QualityChanged`，在 Low/Mid 或 PC Forward/Deferred 切换后同步相机级 HDR、MSAA、阴影、后处理、SMAA 与 Dithering 开关。Unity `AudioListener` 和 FMOD `StudioListener` 始终位于唯一输出相机；`SSGICamera` 只在桌面端创建，实际 SSGI Pass 仅存在于 PC Deferred Mid 管线。

`SSGICamera` 位于预定义 `Assembly-CSharp`，而 `CameraSystem` 位于项目的 `Runtime` 程序集，因此系统通过完整运行时类型名添加该组件，并由同目录 `link.xml` 明确保留它，避免 IL2CPP 构建裁剪。

## 扩展约束

- `FilmSystem` 通过 `AcquireFilmCamera` 获取一次性的演出镜头优先级租约。租约释放后，`CameraSystem` 恢复该镜头原优先级；演出代码不得直接修改输出 `Main Camera` 或长期改变玩法跟随镜头优先级。

- 后续增加锁定、冲刺、演出或场景镜头时，应新增 Cinemachine Camera 并由 `CameraSystem` 仲裁优先级，不能把 Camera 放回角色 Prefab。
- 普通换人继续复用当前跟随镜头，只替换跟随目标，保证位置交接和相机控制权各自只有一个来源。
- 如果修改基础构图，应集中调整 `FollowLocalPosition` 与 `FollowLocalRotation`，不要在角色资源中保存重复参数。
