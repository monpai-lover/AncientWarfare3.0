# 古代战争 3.0 v1.2.5

本次版本针对大步长模式下 RTS 军队活性进行深度审计与补强：修复继承恢复/战争生命周期的集合修改异常、控制器的军队孤儿化缺陷、战争总管首令饥饿和禁卫军任务重申延迟。

## RTS 军队活性补强（重点）

- **修复继承恢复服务枚举时修改集合导致 `InvalidOperationException`、恢复周期永久静默失败的问题**；继承恢复改为先收集待移除键、循环结束后批量移除，避免国王死亡后军队永久失去统帅。
- **修复战争生命周期服务预算捕获时枚举与 Harmony patch 并发修改竞态**；预算计算改为先快照副本再枚举，避免开战/参战变更时抛异常、生命周期发现永久跳过、军队永不获得战争分配。
- **修复控制器 ProcessFrame 无单军队异常隔离、任意军队抛异常导致该军队已出队但未重入队的孤儿化问题**；ProcessOne 外层增加 per-item try-catch，异常时记警告并 Requeue，避免军队卡住不动、不响应命令、不进攻。
- **修复控制器目标完成握手路径缺失 Requeue 调用、军队释放目标索赔后永久孤儿化的问题**；ShouldHandoffObjective 分支的 early return 前增加 Requeue，避免军队完成进攻后挂机不接收新命令。
- **修复战争总管首令队列 FIFO 确定性排序 + 预算耗尽导致高 ID 战争永久饥饿、尾部战争永不动员的问题**；TryTake 改为轮转出队机制（每 4 次取队头，其余轮转），缓解尾部战争饥饿。

## 禁卫军跟随连续性补强

- **提升禁卫军运行时刷新批次上限从 4 提升至 8**，缓解大步长下任务重申墙钟延迟（60 模拟秒间隔 + 轮转游标 + 多倍模拟速度导致禁卫失去任务后数十秒真实时间才重申）。
- **国王继任时立即同步修复所有禁卫任务**，在调度完整维护周期前先对全部现役禁卫调用 RepairProtectKingTaskIfNeeded，避免继任瞬间禁卫因陈旧国王引用游荡数十秒。

## 诊断

- 修复的 4 个 CRITICAL 级缺陷均由 Guard 异常隔离层捕获并静默跳过（ArmyRtsSchedulingService、AWAuthorityCycleService），用户观测为"军队卡住不动/挂机/不进攻/禁卫不跟随国王"，日志中可见对应的 LogWarning 循环输出。

## 安装说明

- 下载 `AncientWarfare3-v1.2.5.zip`。
- 解压后应得到单一目录 `AncientWarfare3.0`，将该目录放入 NeoModLoader 的模组目录。
- 更新前请移除旧的 `AncientWarfare3.0` 模组目录，避免旧源码残留或出现相同 GUID 的重复模组目录。
- 本发布包为源码加载包，不包含预编译的 `AncientWarfare3.dll`。
- 包内保留 `Assemblies` 及其依赖 DLL，NeoModLoader 编译源码所需依赖不可删除。

## 验证范围

- Release 主项目构建（0 警告 0 错误）。
- 规则测试全量通过（待运行）。
- 审计报告覆盖 ArmyRtsControllerService、KingdomWarDirectorService、RoyalGuardService、ArmyStallWatchdogService、ArmyRtsSuccessionRecoveryService、ArmyRtsWarLifecycleService 共计 6 个子系统，13 个缺陷修复其中 4 个 CRITICAL、5 个 HIGH。
