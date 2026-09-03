# 历史人物抽卡系统设计

**日期：** 2026-09-04  
**状态：** 草案，待用户审核

## 1. 目标

在 AncientWarfare3 的夏人谱系入口中增加一个独立的历史人物抽卡系统。玩家可以单抽历史人物，观看参考 CS2 case simulator 的开箱动画和音效，在全局收藏仓库查看人物资料，并把已抽到的人物部署到当前世界的某座城市，使该城市脱离原王国、以历史国号建立新王国，由该人物担任国王。

本功能必须和现有的“历史人物自动降临”分开：

- 抽卡、收藏数量和抽卡记录不写入 `FigureStateStore`。
- 自动降临的顺序、互斥和存档状态不因玩家抽卡而改变。
- 卡片部署人物有独立的身份标记，自动降临逻辑不能把它当成候选人物，也不能在同一世界对同一身份重复自动生成。
- 既有族谱、传记、官职历史、外交历史和编年史继续使用当前世界的 Lineage SQLite 存档库。

## 2. 已确定的玩法规则

### 2.1 卡池和稀有度

卡池由两部分组成：

1. 现有 `HistoricalFigureDef.All` 中的历史人物。
2. 中国历代皇帝目录。目录从秦始皇至清宣统，包含通常中国皇帝名录中的正统王朝、追尊皇帝、改朝皇帝和有明确称帝记录的主要割据政权君主。没有皇帝称号的诸侯、将领和仅有传说依据的人物不因“所有皇帝”而强行加入。

如果现有人物和皇帝目录是同一个历史人物，使用一个稳定卡片 ID 合并，不能因为两个来源产生重复卡。每张卡都必须有唯一 `CardId`，目录构建时验证重复 ID、空姓名、空国号和无效父母引用。

五档名称、颜色和概率从低到高固定如下，颜色沿用参考工程：

| 稀有度 | 显示名 | 颜色 | 单抽概率 |
| --- | --- | --- | ---: |
| 金 | 帝统 | `#FFD700` | 0.26% |
| 红 | 雄主 | `#eb4b4b` | 0.64% |
| 粉 | 显赫 | `#d32ce6` | 3.20% |
| 紫 | 名世 | `#8847ff` | 15.98% |
| 蓝 | 史载 | `#4b69ff` | 79.92% |

五档概率合计 100%。

稀有度是卡片目录中的稳定属性，按人物历史影响力和史料地位人工编定；抽卡先按五档概率取稀有度，再在该档卡池中均匀取一人。目录不能通过“该档没有卡时退回其他档”改变概率，初始化校验失败时只禁用抽卡并写日志。

### 2.2 名气排序

每张卡保存 `FameScore`，范围为 0 到 100。收藏仓库、卡池目录和人物选择列表默认按以下键排序：

1. `FameScore` 降序。
2. 历史生年或即位年份升序。
3. `CardId` 逐字节升序。

稀有度和名气排序是两个字段，不能用 UI 颜色代替名气分数。人物详情必须显示姓名、朝代、历史国号、时代、名气等级、父亲、母亲和简短生平；父母没有明确史料时显示“史料不详”，不根据姓氏、朝代或后世传说自动编造。

### 2.3 收藏和重复卡

收藏是玩家级数据，跨世界、跨存档保留。抽到重复人物时只增加持有数量和抽卡记录，不消耗部署资格，也不限制同一人物多次部署。部署不会减少收藏数量，因此玩家可以重复部署同一张卡。

收藏文件路径固定为：

```text
Application.persistentDataPath/AncientWarfare3/historical_figure_cards.json
```

文件保存：

- `schemaVersion`
- 每张 `CardId` 的 `ownedCount`
- 最近抽卡记录：`drawId`、`CardId`、稀有度、UTC 时间
- `lastUpdatedUtc`

抽卡状态使用内存缓存并在写盘时加锁。写入采用临时文件加替换的原子流程；新目录不存在时创建目录。文件损坏时保留损坏文件副本、记录警告并加载空仓库，不让 UI 或游戏主循环崩溃。抽卡事务的顺序固定为：生成结果、写入收藏成功、再启动动画。写盘失败时不显示成功结果，也不播放揭示音。

## 3. 玩家流程和状态机

入口位于现有 `AW_LineageTab`，新增“历史人物抽卡”按钮。窗口中使用两个同级视图：

- `抽取`：显示稀有度说明、可抽取按钮、最近结果和目录浏览入口。
- `仓库`：显示按名气排序的全部卡片；每项显示持有数量、稀有度、姓名和历史国号。

单抽流程如下：

1. 玩家点击单抽。
2. `HistoricalFigureCardDrawService` 在主线程确定唯一结果和 50 张滚动卡片的内容，并先提交收藏文件。
3. UI 进入滚动状态，中奖卡固定在索引 `42`。
4. 动画结束或玩家点击跳过后，进入结果详情。
5. 详情页提供“部署到城市”和“关闭”操作；重复卡仍显示可部署。
6. 点击部署后进入地图城市选择状态，窗口暂时隐藏，取消时不改变任何世界状态。
7. 点击城市先显示目标城市、原王国、将使用的历史国号和人物名称；确认后执行建国。
8. 建国成功后回到人物详情，显示新王国和部署时间；失败则恢复选择状态并显示明确错误。

状态机固定为：

```text
Idle -> Rolling -> Reveal -> Details
Details -> Placement -> PlacementConfirm -> Deploying -> Details
Placement -> Idle
```

`Rolling` 中再次点击抽取、部署或关闭均无效。跳过只缩短视觉过程，不能重新随机，也不能改变已落盘的结果。新世界、读档和切换世界时清除未完成的 `Placement` 状态，不清除收藏。

## 4. 卡片数据模型

新增 `HistoricalFigureCardDefinition`，不修改 `HistoricalFigureDef` 的注册索引语义。每条定义包含：

```text
CardId
DisplayName
FamilyName
ClanName
GivenName
DynastyName
HistoricalKingdomName
HistoricalEra
BirthYear
DeathYear
FameScore
Rarity
Sex
Biography
FatherCardId / FatherDisplayName
MotherCardId / MotherDisplayName
PortraitPath
LegacyFigureId / LegacyRegistryIndex
CombatHealth
CombatTraits
```

`HistoricalKingdomName` 直接保存历史短国号，例如“汉”“魏”“赵”，部署时不能根据地理位置自动添加“前、后、东、西、南、北”等前缀。`PortraitPath` 允许为空，UI 使用夏人国王图标作为稳定后备图，不因缺少一张画像阻止抽卡。

父母关系分三种状态：

- 父母卡片 ID 有效：详情可以跳转到父母卡片。
- 只有明确的历史显示名：详情显示名字，部署时创建显示用合成祖先档案。
- 没有明确史料：ID 和名字均为空，详情显示“史料不详”，不创建祖先档案。

卡片目录在初始化时做完整性检查：唯一 ID、唯一历史身份映射、稀有度合法、名气分数在范围内、所有父母引用存在、国号不包含自动地理前缀。测试夹具必须覆盖当前 91 人和皇帝目录中的代表性人物，并由目录快照测试防止无意删除历史人物。

## 5. 部署建国

### 5.1 部署前检查

`HistoricalFigureCardDeploymentService` 只在 Unity 主线程执行，且一次只允许一个部署事务。确认前检查：

- 世界、城市、城市数据和现存城市王国均有效。
- 目标城市没有在删除、迁移或读档流程中。
- Lineage archive 可用。
- 夏人成人 actor 资产可创建。
- 卡片 ID 在目录中存在。
- 当前没有另一个抽卡部署事务。

目标城市必须是现存文明城市；无主城市、已删除城市和读档过渡期城市拒绝部署。选择城市不会先改变其归属。

### 5.2 建国顺序

成功部署使用当前 WorldBox 引擎流程，并在适配层内固定顺序：

1. 记录目标城市、旧王国、旧首领和旧首都等回滚信息。
2. 进入 `HistoricalFigureCardDeploymentScope`，抑制 `AW_FigurePatch` 的自动降临回调。
3. 创建夏人成人 actor，先设置性别、年龄、姓名、家族/氏族、健康和历史人物标记；不能创建 baby 后再修正，否则会重现地图头部缩放问题。
4. 让 actor 加入目标城市。
5. 调用 `city.makeOwnKingdom(actor, true, false)` 创建割据王国。
6. 设置新王国首都为目标城市，并将新王国名称设置为卡片的 `HistoricalKingdomName`。
7. 通过卡片身份服务写入 `CardId`、`drawId`、`deploymentId` 和历史国号，不写入 `FigureStateStore`。
8. 通过通用历史父母入口写入明确的父亲、母亲和显示名；引擎父母槽保持不参与现实繁殖的安全状态，避免人物被错误接入当前家庭。
9. 调用既有贵族/国王谱系初始化和历史写入入口，记录人物出生于世界、成为国王、卡片部署建国和城市归属变更。
10. 退出作用域并刷新名称、地图图标、家族树和历史 API 事件队列。

历史人物的国家使用卡片中保存的历史国号，不使用人物名、城市名或地理方位自动拼接。新王国只在选定城市建立，邻近城市不在此次部署中连带转移。

### 5.3 失败恢复

部署方法返回结构化的成功/失败结果，不把异常直接抛到 UI。`Deploying` 任意阶段失败时：

- 收藏数量不减少，抽卡记录不回滚，因为它已经是玩家获得的卡片。
- 如果尚未建国，只删除新建 actor。
- 如果已经创建新王国，恢复目标城市原王国、首领和首都关系，删除未完成的新王国和 actor，并清除本次临时历史行。
- 如果历史数据库提交失败，不显示建国成功；成功的世界对象和历史写入必须同时达到提交条件。
- 回滚再次失败时记录完整的旧王国 ID、城市 ID、actor ID 和异常，停止继续重试，避免产生更多半成品。

部署操作使用唯一 `deploymentId` 去重，确认按钮和地图重复点击不能创建两个同一事务的人物。

## 6. 与现有历史系统的连接

新增卡片身份服务，并把卡片身份作为独立历史来源：

- `AW3_CARD_ID`
- `AW3_CARD_DRAW_ID`
- `AW3_CARD_DEPLOYMENT_ID`

自动降临服务增加卡片身份检查：卡片人物是独立收藏来源，不占用自动降临的 `FigureState` 槽位，但其存活 actor 不能被自动降临再次识别为普通候选。自动人物的旧存档、顺序和互斥规则保持不变。

历史父母服务增加通用的外部人物入口，复用 `HistoricalAncestorService` 的清理、显示名和合成祖先档案逻辑。卡片父母的合成 ID 必须由 `deploymentId + parentSlot` 唯一生成，不能与当前 91 人或学校宗师的合成 ID 冲突。

建国和人物事件通过现有 `HistoryWriter` / `ChronicleEvents` 的提交路径写入 `PersonBiography`、`KingdomHistory` 和 `CityHistory`。只有写入成功后才调用现有 `AW3HistoryEventPublisher` 的提交通知，使已经接入 `AW3HistoryApi` 的其他历史 AI 模组能收到卡片部署事件。建议事件类型为：

```text
card_deployed
card_king
card_kingdom_founded
```

事件内容包含 `CardId`、`DrawId`、`DeploymentId`、历史国号和目标城市 ID，但公共 API 不暴露 Unity `Actor`、`Kingdom` 或 `City` 对象。

## 7. 开箱 UI、动画和音效

### 7.1 视觉结构

Unity UI 复用现有 `AbstractWindow`、`ScrollWindow`、`AW_UIStyle` 和窗口 ID 管理，不引入网页运行时或前端框架。新增窗口采用固定约束尺寸，窄屏下压缩卡片数量和文字，不让动态姓名改变卡片宽高。

滚动区域复刻参考工程的主要行为：

- 横向约 50 张卡片。
- 中奖卡位于索引 `42`，两侧卡片不会使用同一张中奖卡。
- 背景轨道轻微模糊，左右边缘渐隐。
- 中央圆形放大区域和中央定位线保持在最上层。
- 普通动画约 6000ms，减速曲线为 `cubic-bezier(0.1, 0.4, 0.4, 1)` 对应的 Unity 插值实现。
- 结束时中央卡片放大并显示稀有度色带，随后展示人物详情。
- 每张卡片越过中央位置只触发一次滚动音。
- 跳过按钮直接把轨道定位到同一结果并触发一次揭示音。

卡片项显示画像、姓名、历史国号和稀有度色带。人物详情采用完整窗口层，不将详情塞进滚动卡片内部；详情中显示父母、朝代、年代、生平和部署按钮。

### 7.2 音效

参考工程中的音效源位于本地参考仓库的 `frontend/assets/audio`。不复制其前端代码，只复刻开箱时序，并将实际使用的音效转换为 WorldBox 当前资源扫描器能识别的 WAV：

```text
GameResources/sounds/historical_cards/aw3_card_unlock.wav
GameResources/sounds/historical_cards/aw3_card_unlock_immediate.wav
GameResources/sounds/historical_cards/aw3_card_scroll.wav
GameResources/sounds/historical_cards/aw3_card_button_press.wav
GameResources/sounds/historical_cards/aw3_card_item_hover.wav
GameResources/sounds/historical_cards/aw3_card_reveal_blue.wav
GameResources/sounds/historical_cards/aw3_card_reveal_purple.wav
GameResources/sounds/historical_cards/aw3_card_reveal_pink.wav
GameResources/sounds/historical_cards/aw3_card_reveal_red.wav
GameResources/sounds/historical_cards/aw3_card_reveal_gold.wav
```

`HistoricalFigureCardAudioService` 通过 `CustomAudioManager` 播放，服从游戏音效开关和音量设置。音效缺失、转换失败或播放异常只降级为无声，不阻塞抽卡和部署。第三方音效的来源和许可信息写入 `THIRD_PARTY_NOTICES.md`；发布包不包含参考仓库的源码和未使用的素材。

## 8. 建议文件边界

新增：

```text
Code/content/figures/HistoricalFigureCardModels.cs
Code/content/figures/HistoricalFigureCardCatalog.cs
Code/core/lineage/HistoricalFigureCardCollectionStore.cs
Code/core/lineage/HistoricalFigureCardDrawService.cs
Code/core/lineage/HistoricalFigureCardDeploymentRules.cs
Code/core/lineage/HistoricalFigureCardDeploymentService.cs
Code/core/lineage/HistoricalFigureCardIdentityService.cs
Code/core/lineage/HistoricalFigureCardParentageService.cs
Code/core/lineage/HistoricalFigureCardAudioService.cs
Code/patch/AW_HistoricalFigureCardPatch.cs
Code/ui/windows/HistoricalFigureDrawWindow.cs
Code/ui/items/HistoricalFigureCardListItem.cs
```

按职责修改：

```text
Code/ModClass.cs 或 Code/content/XiaContent.cs  # 初始化目录、收藏和音效
Code/ui/AW_LineageTab.cs                         # 新增入口按钮
Code/ui/AW_LineageWindowIds.cs                   # 新窗口 ID
Code/patch/AW_FigurePatch.cs                     # 卡片部署作用域和身份保护
Code/core/lineage/HistoricalAncestorService.cs  # 外部卡片父母入口
Code/core/lineage/LineageKeys.cs                 # 卡片身份键
THIRD_PARTY_NOTICES.md                           # 音效来源说明
```

图形和音效资源只放入 `GameResources` 的明确子目录，不修改已有 Xia 人物、建筑和港口贴图。

## 9. 测试和验收

### 9.1 纯规则测试

在 `Tests/AncientWarfare3.Rules.Tests` 增加独立测试源，并在 `Program.cs.txt` 增加专用开关：

- `HistoricalFigureCardCatalogRulesTests.cs.txt`：ID 唯一、父母引用、国号前缀校验、当前 91 人映射和皇帝目录快照。
- `HistoricalFigureCardDrawRulesTests.cs.txt`：五档概率总和、注入随机源后的档位选择、档内选择、中奖索引 42、跳过不改结果。
- `HistoricalFigureCardCollectionRulesTests.cs.txt`：重复卡计数、原子写入决策、损坏文件降级和跨世界不清空。
- `HistoricalFigureCardDeploymentRulesTests.cs.txt`：无主/删除城市拒绝、成人 actor 前置、单事务去重、历史短国号保持不变和失败不报告成功。
- `HistoricalFigureCardParentageRulesTests.cs.txt`：父母 ID 槽位唯一、史料不详不创建祖先、父母卡片引用保持稳定。
- `HistoricalFigureCardSourceGuardTests.cs.txt`：抽卡代码不调用 `FigureStateStore` 写入，不使用地理国号前缀拼接，不在后台线程触碰 Unity 世界对象。

每个规则遵循 TDD：先添加会失败的测试，运行专用测试开关确认失败原因，再写最小生产代码，最后运行专用测试和全量规则测试。

### 9.2 游戏内验收矩阵

至少手动验证：

1. 新安装、空收藏、已有收藏和损坏收藏文件。
2. 五种稀有度结果的颜色、滚动音、揭示音和详情内容。
3. 正常动画、跳过动画、重复点击和窗口关闭重开。
4. 同一人物重复抽取、重复部署和跨世界读档。
5. 在普通王国、夏王国和已有首都选择城市部署。
6. 部署后地图头部、国王身份、短国号、城市归属、家族树、人物传记和编年史。
7. 历史父亲、母亲已知/未知三种情况。
8. 部署中途保存、读档、切换世界和模拟数据库不可用。
9. 自动历史人物开关开启和关闭时，抽卡部署都不会改变自动降临顺序。

既有规则测试工程当前已有与本功能无关的缺失类型阻断；验证报告必须将这些基线错误和抽卡新增错误分开记录，不能把基线阻断标成抽卡通过。

## 10. 不包含的内容

- 不做多抽、十连抽、自动抽取或经济收费。
- 不把卡片数量当成一次性消耗品。
- 不把抽卡人物写入自动降临的 `FigureStateStore`。
- 不为没有明确史料的父母生成猜测人物。
- 不复制 CS2 项目的前端源码、数据库、网页或无关图片。
- 不在本功能中修改既有 Xia 建筑、人物和港口贴图逻辑。
