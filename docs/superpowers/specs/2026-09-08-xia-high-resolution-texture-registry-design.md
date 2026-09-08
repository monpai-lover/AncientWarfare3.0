# Xia 高清贴图登记与切换工具设计

## 目标

为 Xia actor 身体贴图建立一个统一的高清化登记表。当前只登记已经按高清规格制作的国王贴图；后续新增或切换 `male_1`、`female_1`、`warrior_1`、`leader_1` 等贴图时，只需增加一条登记或调用切换接口，不再修改地图动画、帧偏移和头像面板的多处补丁。

## 方案

新增 `XiaHighResolutionTextureRegistry`，作为所有高清贴图判断的唯一入口。登记项使用规范化后的动画贴图目录作为键，并保存：

- `Enabled`：该目录当前是否启用高清处理；
- `ResolutionFactor`：身体 PNG 相对普通分辨率的倍率，当前为 4；
- `ScaleAvatar`：头像面板是否同步抵消该倍率；
- `ScaleFrameOffsets`：动画帧的头部、手持物和单位尺寸偏移是否同步换算。

登记表不依赖目录名后缀猜测，未知路径保持原版行为。路径比较统一把反斜杠转换为正斜杠并去掉首尾分隔符，避免 Windows 与资源路径格式差异造成漏判。

## 对外使用方式

注册与切换集中在规则类中，调用方不直接访问字典：

- `Register(path, factor, scaleAvatar, scaleFrameOffsets, enabled)`：添加或更新一项；
- `SetEnabled(path, enabled)`：一键开关已登记目录；
- `TryGet(path, out profile)`：按动画目录读取配置；
- `IsEnabled(path)`、`ResolveFactor(path, fallback)`：提供补丁所需的轻量查询。

初始注册项为 `actors/species/civs/Xia/king` 和 `actors/species/civs/Xia/king_han`，倍率均为 4，头像与帧偏移同步启用。登记操作应是幂等的，重复注册不会产生重复缓存或重复缩放。

## 数据流

1. `AW_ActorVisualRolePatch` 选择 Xia actor 的身体目录。
2. `AW_HighResolutionSpritePatch` 在动画容器创建完成后向登记表查询；命中启用项时登记身体 Sprite、按 profile 倍率重建 PPU，并换算帧偏移。
3. `AW_XiaKingScalePatch` 在头像加载和 `setImageParams` 时查询 actor 对应的身体目录；命中且 `ScaleAvatar` 为真时抵消同一倍率。
4. 未命中、禁用或配置无效时，两个补丁都保留原版缩放行为。

为避免跨角色误用，头像查询需要同时确认 actor 是 Xia、成年且为国王；动画容器查询只依据完整规范化路径。

## 错误处理与性能

- 登记表在类型初始化时构建，查询为字典 O(1)，不在每帧扫描磁盘或反射。
- 无效路径、倍率小于等于 0 或非 Xia 路径不启用高清配置，并回退到调用方提供的默认倍率。
- Sprite 集合和帧偏移只在动画容器首次创建时处理；现有缓存语义保持不变。
- 运行时切换只影响后续创建的容器/头像，必要时由调用方清理对应缓存，不对已有 Sprite 重复缩放。

## 测试边界

规则测试覆盖：

- `king`、`king_han` 的默认登记与倍率；
- Windows 反斜杠、首尾分隔符的路径归一化；
- 未知路径和禁用项回退普通渲染；
- 重复注册为更新而非新增；
- 非法倍率拒绝启用；
- `SetEnabled` 切换只改变登记状态，不改变其他目录。

## 不在本次范围

- 不自动把所有 Xia 贴图转换为高清资源；
- 不修改现有贴图文件或生成新的 PNG；
- 不把高清状态持久化到存档；
- 不部署或依赖 `AncientWarfare3.dll`，仍由 NeoModLoader 编译 `Code`。
