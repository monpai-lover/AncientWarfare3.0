# 外交要求称臣一级爵位门槛设计

## 目标

玩家与普通外交 AI 发起“要求称臣”时，只要求拟任宗主的爵位严格高于目标至少一级；不再以宗主军力达到目标两倍作为提案发送前的硬门槛。AI 发动附庸战争的既有跨级爵位条件保持不变。

## 根因

外交菜单初始可用性通过 `VassalService.CanSetVassal` 判断，当前已经允许任意严格高一级关系。但选择具体 `vassalize_demand` 后，`DiplomacyProposalService.AssessVassalizationWithSelectionCore` 又额外要求请求方军力至少为目标两倍，否则返回 `insufficient_power`。因此同一按钮会先显示可用，选中后又因另一套条件失效。

## 行为

- 保留和平、非同盟、双方独立、直接接壤、无循环附庸、非叛军等现有条件。
- 保留 `VassalService.CanSetVassal(target, requester)` 的严格高爵位判断；侯可要求伯、公可要求侯，以此类推。
- 删除 `vassalize_demand` 创建前的二倍军力硬拒绝。
- 军力、关系、外交能力和局势继续进入接受度评分；弱小请求方可以发出提案，但目标通常会拒绝。
- 普通外交 AI 仍使用 predicted acceptance 过滤不会被接受的提案，不增加垃圾请求。
- `WarAiGoalSelectionRules.CanAiForceVassal` 与 AI 附庸战争选择、宣战条件均不得修改。

## 测试

1. 宗主高一级、军力不足两倍时，外交要求称臣允许创建并返回接受度结果。
2. 同爵位或低爵位仍返回 `title_too_low`。
3. 不接壤、已有附庸、战争中或同盟状态仍按原原因拒绝。
4. 玩家可以发送预期拒绝的提案；普通 AI 仍不发送 predicted rejection。
5. AI 附庸战争的一等级差案例继续被拒绝，原合法案例保持不变。

不编译主模组 DLL；使用规则测试和源码守卫验证。
