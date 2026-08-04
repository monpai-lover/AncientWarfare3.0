# 层级附庸地图占领状态显示设计

## 目标

新的层级附庸 MapMode 在保留现有宗主—附庸归属底色、国名和城市名显示的同时，直接复用原版 Kingdom MapMode 的城市占领纹理。

## 根因

层级附庸地图的领土区块已经调用 `MetaTypeLibrary.kingdom.draw_zones`，但原版占领状态并不由该委托绘制。原版通过独立的 QuantumSprite `capturing_zones` 绘制正在被占领的城市区域。

层级附庸模式当前使用自定义 MetaType，因此存在两道阻断：

1. `Zones.showKingdomZones()` 返回 `false`，使原版 `drawCapturingZones` 提前退出。
2. minimap QuantumSprite 白名单不包含 `capturing_zones`，现有清理补丁会移除该图层。

## 设计

### 原版绘制复用

- 给 `Zones.showKingdomZones` 增加兼容 Postfix。
- 当且仅当 `HierarchicalVassalMapModeService.IsActive()` 为真时，将结果设为 `true`。
- 其他地图模式完全保留原版返回值。

这会让原版调用链自行绘制占领状态：

`QuantumSpriteManager.update -> QuantumSpriteLibrary.drawCapturingZones -> CapturingZonesCalculator -> p_mapZone_lines`

颜色、占领进度、区域扩展次序、动画和排序均由原版负责。

### minimap 保留

将 `capturing_zones` 加入层级附庸地图的 QuantumSprite 保留名单，防止 minimap 清理逻辑误删原版占领图层。

### 明确不做

- 不自行遍历 `World.world.cities`。
- 不手动调用 `drawCapturingZones`。
- 不直接调用 `CapturingZonesCalculator.getListToDraw`。
- 不创建新的占领纹理或颜色算法。
- 不恢复原版 NameplateManager；原版城市铭牌仍保持隐藏，避免与 AW3 自定义国名和城市名重叠。

## 性能约束

该功能只恢复原版已有的稀疏 `capturing_zones` 绘制，不触发额外的全图 Zone 重算，不改变层级附庸地图的领土缓存与文字缓存。

## 测试

新增或扩充规则测试，验证：

1. 层级附庸模式激活时能够通过原版 Kingdom 可见性门槛。
2. 非层级模式不改变 `Zones.showKingdomZones` 的原始结果。
3. `capturing_zones` 被 minimap 白名单保留。
4. 实现中没有自绘占领区、全城市遍历或手动调用原版占领绘制入口。

不编译主模组 DLL；使用规则测试和源码守卫验证。
