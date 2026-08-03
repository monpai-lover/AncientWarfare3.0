# 权能取消按钮图标残留修复设计

## 问题

选择 AW3 GodPower 后，`AW_PowerButtonVisualPatch` 会同时设置取消按钮 Image 的 `sprite` 与 `overrideSprite`。之后选择原版 GodPower 时，原版 `CancelButton.setIconFrom` 只更新 `sprite`，Unity 仍优先显示上一次 AW3 写入的 `overrideSprite`，导致取消按钮继续显示 AW3 图标。

## 修复

继续只对 `xia` 和 `aw_*` 权能使用 AW3 自定义图标路径。对具有有效 ID 的非 AW3 GodPower，在允许原版 `setIconFrom` 执行前先把取消按钮的 `overrideSprite` 清空；原版随后按原有逻辑写入 `sprite`。

纯规则层新增“是否需要清除 AW3 override”判断，Harmony 补丁只负责把规则应用到 Unity Image。空 ID 或无效按钮不主动改变图标，保持原版容错语义。

## 验收

- `aw_*` 与 `xia` 继续使用按钮的 override 图标。
- 从 AW3 GodPower 切换到原版 GodPower 时清除旧 override，显示原版图标。
- 从一个 AW3 GodPower 切换到另一个 AW3 GodPower 时不经过原版清理路径。
- 空 ID 不清除当前图标，并交由原版处理。
- 现有完整规则测试保持通过。

## 非目标

- 不接管所有原版 GodPower 的图标选择。
- 不改变权能 Tab 的布局、显隐或按钮父级。
- 不修改 AW3 GodPower 的选择与取消逻辑。
