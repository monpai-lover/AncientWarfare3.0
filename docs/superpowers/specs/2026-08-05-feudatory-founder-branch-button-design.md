# 藩王建立氏支按钮修复设计

## 目标

藩王本人建立的 `feudatory` 氏支在家族树节点显示分支按钮，并能展开/定位该氏支；继承藩国的后任藩王不得冒认建立者。旧存档自动恢复，无需迁移。

## 根因

藩王建支已创建 `ShiBranch`，但只写 `SHI_ID` 与 `FEUDATORY_BRANCH_SHI_ID`，没有写通用 `FOUNDED_BRANCH_SHI_ID`。家族树按钮、归档和异步查询又只承认 `king_founded`，因此 `feudatory` 支即使存在也不会进入投影。

## 设计

- 新建藩王氏支时，仅当 `FOUNDER_ACTOR_ID` 等于当前藩王本人，写入 `FOUNDED_BRANCH_SHI_ID`。
- 复用已有氏支时同样校验创建者本人；继任藩王只继承氏支身份，不获得“建立分支”按钮。
- 异步查询、旧档恢复和分支显示校验接受 `king_founded` 与 `feudatory` 两种来源。
- 所有恢复均要求 `FOUNDER_ACTOR_ID == ACTOR_ID`；多个候选按创建时间选择最近有效支。
- pending archive 中的 `-1` 不得覆盖 bulk 查询已经恢复的有效建立分支 ID。
- 按钮点击继续复用现有 `OpenBigTree(branchShiId)` 路径。

## 测试

- 藩王本人建立 `feudatory` 支时显示按钮并能打开。
- 继任藩王 `FOUNDER_ACTOR_ID != actor` 时不显示按钮。
- 旧档 `ActorArchive.FOUNDED_BRANCH_SHI_ID=-1` 但存在本人建立的 `feudatory` 支时自动恢复。
- 原有 `king_founded` 行为保持。
- 其他来源氏支不被误识别。
- pending `-1` 不覆盖 bulk 恢复值。

不编译主模组 DLL。
