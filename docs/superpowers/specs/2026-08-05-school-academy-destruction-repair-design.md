# 学院建筑销毁清理与原地修复设计

## 目标

学院建筑被摧毁后立即清理失效的实体引用、施工指针、场地占用和地图缓存；学派机构进入暂时停办状态，并通过有界错峰队列优先在原 Tile 重建。重建成功后恢复同一机构，避免重复计数和历史断裂。

## 根因

AW3 没有监听学院建筑的 `startMakingRuins`、`startDestroyBuilding`、`removeBuildingFinal` 生命周期。现有学院服务只缓存 `cityId -> Building` 和年度尝试年份，不保存旧位置，也没有独立修复队列。数据库机构与实体建筑没有绑定，建筑毁坏后 ACTIVE、地图地标和场地 claim 仍可能残留。

## 建筑生命周期

- 只匹配学院专用 building ID/type，不影响普通 `library` 和其他建筑。
- 在建筑尚未清除 Tile 前捕获 city、building ID、tile x/y、所属国家与机构 ID。
- 销毁/废墟确认后执行幂等清理：
  - 从学院实体缓存移除。
  - 若它是 `city.under_construction_building`，清空该指针。
  - 释放该城所有学院 venue claim 与 `OccupiedByCity`。
  - 标记 SchoolLandmark 缓存 dirty。
  - 将关联机构置为“修缮中/暂时停办”，但保留历史和学派归属。
  - 每城最多创建一个 repair ticket。

## 修复队列

- 不在建筑受击/销毁调用栈中直接创建建筑。
- 使用有界、错峰、幂等的 `AcademyRepairQueue`。
- 第一选择为记录的原 Tile；若 Tile 已被占用、离开城市、变成水面或不再适合建筑，才回退现有 `FindPlacement`。
- 原版人口、总建筑数和资源门槛不阻断修缮中的学院；修复队列只要求城市仍有效并拥有可用建设位置。
- 城市被彻底销毁时取消 ticket；城市换国时将 ticket 重新绑定到当前城市/国家，不重复创建机构。
- 读档恢复时根据“机构修缮状态 + 无有效学院实体”重建唯一 ticket。

## 有效实体判定

`IsLiveAcademyForCity` 必须拒绝 `isOnRemove`、`isRemoved`、废墟及不可用建筑。施工中的有效学院可继续视作存在，但进入销毁流程后必须立即失效。

## 机构状态

- 建筑毁坏时机构不删除，改为暂时停办/修缮中，停止开坛讲学和正常招生。
- 修复成功后恢复 ACTIVE，并只恢复一次 ledger/institution 计数。
- 若没有可靠的实体绑定字段，新增最小 physical binding/state 持久化，记录 building ID、tile x/y 与 repair state；不得仅靠 `SchoolInstitution.ACTIVE` 猜测实体是否存在。

## 测试

- `Normal + OnRemove` 的施工学院不再被判定 live。
- ruins/removeFinal 捕获旧 Tile，只入队一次。
- 清理施工指针、实体缓存、venue claim 和 landmark cache。
- 修复优先原 Tile，原地不可用才 fallback。
- 城市销毁取消 ticket，换国重绑，读档恢复幂等。
- 机构停办/恢复与 ledger 只变化一次。
- 普通 library 和非学院建筑销毁不触发队列。

不编译主模组 DLL。
