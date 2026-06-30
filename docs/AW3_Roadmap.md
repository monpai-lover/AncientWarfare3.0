# AncientWarfare 3.0 — 总进度与路线图

> 本文件是 AW3.0 的**单一进度真相**:整个 mod 计划、已完成的、正在做的、将来要做的。
> AW3.0 = 在新版 WorldBox(0.51.2,NeoModLoader/Harmony/.NET 4.8)上**重写**(不移植) AncientWarfare2.0。
> 主题:中国上古**夏朝**文明 + 天命王朝 + 政治/军事深度系统。
> 完整旧版功能蓝本见 `README.md`;姓族系统设计见 `docs/superpowers/specs/`、`docs/AW3_Lineage_Archive_Task.md`。
>
> 更新日期:2026-06-30

---

## 总览:支柱与状态

| 支柱 | 一句话 | 状态 |
|---|---|---|
| **夏朝文明 (Xia)** | 种族 + 单位贴图 + 建筑 + 命名 + 神力,自成体系 | ✅ 基本完成 |
| **姓族/氏支/家族树** | 姓姬氏姬、氏支分封、继承人、年号、谱系档案 UI、随存档持久化 | ✅ 完成 |
| **历史人物降临** | 姬发→嬴政→刘邦→曹丕→司马炎按序降临、建国名、收藏、小地图图标 | ✅ 完成(合流门依赖未做) |
| **天命系统 (MoH)** | 全世界唯一天命王朝、天命值升降、崩溃天下大乱 | ⏳ 未做(留桩) |
| **政策/阶级系统** | 王国状态机 奴隶制→封建→帝制,状态决定城市 AI | ⏳ 未做 |
| **附庸/宗主 (Vassal)** | 王国成他国附庸,颜色/外交/战争联动,独立战/吞并战 | ⏳ 未做 |
| **奴隶制** | 捕奴→繁殖→奴隶军,独立奴隶经济 | ⏳ 未做 |
| **城市税收与科技** | 非首都向首都缴税,城市研究科技树解锁政策 | ⏳ 未做 |
| **历史记录/编年史** | SQLite 入库,编年史/王国史窗口 | 🟡 部分(姓族档案库已建,编年史窗未做) |
| **中文命名** | 集成"一米中文名",姓/氏/名/国号/年号/城名 | ✅ 完成 |

✅ 已完成 · 🟡 部分完成 · ⏳ 计划中

---

## 一、已完成

### 1. 项目骨架与工具链
- mod.json / csproj(绝对路径引用,`$(WorldBoxManaged)`/`$(NewModDir)`)/ ModClass 入口(`new Harmony(GUID).PatchAll()`)。
- GitHub 仓库 + README 功能蓝图。
- 构建:F 盘 `dotnet build`(`DOTNET_ROLL_FORWARD=Major`)。**只管 F 盘开发目录**,不碰 D 盘部署(用户自己同步)。

### 2. 夏朝 Xia 种族(批A)
- `XiaRace`:clone `$civ_advanced_unit$`,文明属性、地图色 #33724D、寿命/繁衍、**genome 加强**(health130/damage20/warfare5 等,强于 human)、army_multiplier 1.2。
- 逐帧 png 贴图接入(`actors/species/civs/Xia/`,sprites.json 切分);头身分离贴图修复(walk_2_head RectY、PixelBag 负 rect)。
- `XiaKingdom`(王国资产)、`XiaArchitecture`(17 个专属建筑,手动复刻 loadAutoBuildings + re-key + 贴图回退)、`GodPowerLibrary`(生成夏人神力)。
- 旗帜、船(复用 human)、phenotype 肤色。

### 3. 特质(批B)
- 特质组 `aw2`、`aw_social_identity` + 贵族(guizu)、figure、first 等特质。部分 special_effect 依赖天命/政策,留桩。

### 4. 物品(批C)
- `XiaItems`:戟 ji / 戈 ge(青铜长兵,qing 青色斩击特效)/ 孙子兵法 binfa(传奇护符)。
- 武器贴图 pivot(sprites.json 设握把端为手锚点,不改大小)、本地化(translation_key + description,csv 全角逗号)、池武器贴图 null 崩溃修复。

### 5. 姓族 / 氏支 / 家族树(完整后端 + UI)
- **后端**:50/50 氏名规则、氏支分封、继承人、年号、积极建城、同姓不婚。
- **持久化**:SQLite 档案库(反射自动建表 `[TableDef]`),随存档保存/加载(`AW_SavePatch`),死亡归档(`AW_ActorDeathPatch`)。
- **命名**:双轨(中文名 mod 在则接,否则 clone human);姓/氏/名规则;合流前后不同显示名。
- **UI**:姓族总览窗、氏支列表窗、家族树/氏族大树(纵向多叉树、完整 avatar 框含死人重建+灰度、国色名、可拖动无边界)、unit 窗侧栏入口、kingdom 窗年号+爵位+继承人、国家名牌爵位、替换原版家族树。

### 6. 历史人物降临
- 5 人按严格顺序(姬发→嬴政→刘邦→曹丕→司马炎),前一个死后才下一个,存活互斥。
- 随存档持久化(`FigureStateTableItem`),根治 AW2 重进档重复生成。
- 预设姓氏(姬=姓姬/氏姬…)、health1500、自动收藏、figure/first 特质、世界日志降临公告(`$ren$` 国色人名)。
- 成为 king(夺取/继承/创建)时套用预留国名(周/秦/汉/魏/晋)+ 历史改名记录钩子。
- 小地图 `minimap_figure` 图标(Postfix `QuantumSpriteLibrary.drawKings`)。
- tab 开关按钮(默认开)。

---

## 二、当前已知限制 / 待接依赖

| 限制 | 影响 | 解法(后续) |
|---|---|---|
| **姓氏合流国策无触发途径** | 历史人物刘邦/曹丕/司马炎(需合流门)**暂被锁死**;只有姬发/嬴政会出 | 接入政策系统,给 `LineageService.ApplyNameIntegration` 一个 UI/事件触发 |
| **天命系统未做** | `HasMandateKingdom()` 恒 false 桩;历史人物不建天命国(只套国名) | 做天命系统(MoH) |
| 部分特质 special_effect 留桩 | 依赖天命/政策的特质效果暂空 | 随对应系统补 |

---

## 三、将来要做(按 README 蓝图,建议顺序)

1. **天命系统 (MoH)** —— 全世界唯一天命王朝、天命值动态升降、崩溃事件、天命争夺战。历史人物建立天命国的闭环依赖此。
2. **政策 / 阶级状态机** —— 王国 奴隶制→封建→帝制 演进;城市 AI 行为随状态变;**姓氏合流国策**(解开历史人物合流门)。
3. **附庸 / 宗主系统** —— 附庸关系、颜色/外交/战争联动、独立战、吞并战。
4. **奴隶制** —— 捕奴、繁殖、奴隶军、奴隶经济层。
5. **城市税收与科技树** —— 非首都缴税、科技解锁政策。
6. **编年史 / 王国史窗口** —— 复用已建的 SQLite 档案库,做历史事件入库 + 浏览 UI。

---

## 四、关键工程约定(给后续开发)

- **新版 API 真相优先级**:AssetRipper 源码 > 编译验证 > ilspycmd(混淆不可信);**NML 现场编译用的 dll 才是终极真相**(但本项目按用户指示只在 F 盘 build 验证)。
- **随机**:mod 一切随机用私有 `System.Random`,**绝不用 `UnityEngine.Random`**(被 MapBox 世界生成固定播种)。
- **locale csv**:中文字段标点用**全角**;每行半角逗号数必须 = 列头列数,否则 NML 丢弃整个文件。
- **mod 贴图 pivot**:散 PNG pivot 被硬编码画布中心,要自定义用同目录 `sprites.json`(PivotX/Y + PixelsPerUnit)。
- **持久化**:新增 `[TableDef]` 的 `*TableItem` 类即随存档自动建表/迁移,无需手写 SQL。
- **钩点哲学**:优先 Postfix 不接管原方法;少夺舍、多用官方扩展点(Asset 配置、Plot 委托、Harmony patch)。
- 详见记忆库 `aw3-*` 系列。
