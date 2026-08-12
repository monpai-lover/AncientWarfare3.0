# 古代战争 3.0 v1.2.6

本次版本修复大步长模式下 RTS 军队活性的关键缺陷，并添加多层级附庸地图首都视觉标识增强。

## RTS 军队活性修复（重点）

- **修复战争生命周期服务预算捕获时枚举与 Harmony patch 并发修改竞态**；预算计算改为先快照副本再枚举（lock 内快照、lock 外枚举），避免开战/参战变更时抛异常、生命周期发现永久跳过、军队永不获得战争分配。
- **验证继承恢复服务集合修改安全性**；现有代码已使用 defer-remove 模式（先收集待移除键到 toRemove 列表、循环结束后批量移除），避免 foreach 枚举时修改 Pending 字典导致 InvalidOperationException。
- **验证禁卫军国王继任时立即同步修复所有禁卫任务**；OnKingChanged 已对全部现役禁卫调用 RepairProtectKingTaskIfNeeded，避免继任瞬间禁卫因陈旧国王引用游荡。

## 地图模式视觉增强

- **多层级附庸地图模式城市层首都标签黄色高亮**；首都城市标签背景从半透明黑色改为暖黄色（1f/0.95f/0.5f，35% 不透明度），视觉区分首都与普通城市，便于战略态势识别。

## 诊断

- 修复的 WarLifecycleService 竞态由 Guard 异常隔离层捕获并静默跳过（AWAuthorityCycleService），用户观测为"军队卡住不动/不进攻/永不获得战争分配"，日志中可见对应的 LogWarning 循环输出。
- 继承恢复服务和禁卫军继任代码经审计确认已具备正确的异常隔离和集合修改保护。

## 安装说明

- 下载 `AncientWarfare3-v1.2.6.zip`。
- 解压后应得到单一目录 `AncientWarfare3.0`，将该目录放入 NeoModLoader 的模组目录。
- 更新前请移除旧的 `AncientWarfare3.0` 模组目录，避免旧源码残留或出现相同 GUID 的重复模组目录。
- 本发布包为源码加载包，不包含预编译的 `AncientWarfare3.dll`。
- 包内保留 `Assemblies` 及其依赖 DLL，NeoModLoader 编译源码所需依赖不可删除。

## 验证范围

- Release 主项目构建（0 警告 0 错误）。
- 规则测试全量通过。
- 代码审计覆盖 ArmyRtsWarLifecycleService、ArmyRtsSuccessionRecoveryService、RoyalGuardService、HierarchicalVassalMapModeLabelLayer 共计 4 个子系统。
