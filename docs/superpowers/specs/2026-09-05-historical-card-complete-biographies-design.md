# 历史人物卡完整传记设计

## 目标

为运行时历史人物卡目录中的每一张卡提供与该卡身份相符的专属中文背景摘要和详细介绍，彻底移除“相关时代人物”“需结合不同史料互证”等通用占位文案。当前基线为 708 张卡，其中 623 张仍包含通用模板。

## 内容标准

- 以稳定 `CardId` 为唯一键，不按显示姓名查找。
- 同一人物的不同身份卡分别配置。例如 `three_liu_yu` 叙述东晋将领阶段，`song_wudi` 叙述刘宋开国君主阶段。
- 每篇详细介绍至少三句、至少 60 个字符，写入具体战役、制度、作品、官职、政治事件或统治结果。
- 不伪造无法确认的父母、精确年代或个人动机；争议信息使用审慎表述。
- 背景摘要取该卡专属传记第一句，不再由时代、政权和姓名拼接模板。
- 动态赞助者卡继续使用赞助记录与实际贡献生成的个人介绍，不要求写入静态历史传记表。

## 代码结构

新增一个聚合入口和六个时期数据分片：

- `HistoricalFigureCardCuratedBiographies.cs`：合并分片、拒绝重复键、查询详情与首句摘要。
- `HistoricalFigureCardBiographiesPreQin.cs`
- `HistoricalFigureCardBiographiesHan.cs`
- `HistoricalFigureCardBiographiesThreeSix.cs`
- `HistoricalFigureCardBiographiesSuiTang.cs`
- `HistoricalFigureCardBiographiesFiveSong.cs`
- `HistoricalFigureCardBiographiesYuanMingQing.cs`

分片只负责数据，不参与卡池、稀有度或部署逻辑。既有 `HistoricalFigureCardNarratives` 先读取原有精编传记，再读取新分片；所有历史卡均无条目时才保留防御性 fallback，但目录验证会拒绝这种卡进入有效目录。

## 数据流

1. 卡片种子以 `CardId` 请求详细传记。
2. `HistoricalFigureCardNarratives` 查询原有精编表和新聚合表。
3. 详细介绍直接返回专属三句传记，不附加部署说明或通用史料声明。
4. 背景摘要从同一传记提取第一句，保证两处内容一致且人物特定。
5. 目录验证逐卡确认静态历史人物存在专属条目。

## 验收

- 运行时目录总数仍为 708，卡池数量、稀有度和身份不变。
- 688 张非赞助者卡全部具有按 `CardId` 可解析的专属传记；同名不同卡必须各自有键。
- 所有非赞助者详细介绍不少于三句和 60 字，背景摘要不少于 12 字。
- 背景与详情均不得包含已知通用模板片段。
- 不同显示姓名不得共享完全相同的详细介绍；同名不同身份原则上也应使用不同文本。
- 刘裕、安禄山等同名双身份卡有独立断言。
- 专项测试、主工程构建、部署哈希全部通过后才算完成。
