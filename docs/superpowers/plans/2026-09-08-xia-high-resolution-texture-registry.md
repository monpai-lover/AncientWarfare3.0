# Xia 高清贴图登记与切换工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一个集中登记、可一键启停的 Xia 高清贴图工具，并让地图动画与头像面板共用同一套倍率和路径判定。

**Architecture:** 新增 `XiaHighResolutionTextureRegistry` 与不可变 profile，按规范化动画目录 O(1) 查询；初始登记 `king`、`king_han`。动画容器补丁保存每个身体 Sprite 的倍率，头像补丁通过 Xia/国王角色路由取得同一 profile；未知或禁用路径保持原版行为。

**Tech Stack:** C#/.NET Framework 4.8、Harmony、Unity `Sprite`/`UnitAvatarLoader`、现有无框架规则测试项目。

---

### Task 1: 建立高清 profile 与中央登记表

**Files:**
- Create: `Code/core/presentation/XiaHighResolutionTextureRegistry.cs`
- Modify: `Code/core/presentation/XiaKingScaleRules.cs:1-105`

- [ ] **Step 1: 定义 profile 数据对象和登记 API**

新文件定义 `XiaHighResolutionTextureProfile`，属性为 `Path`、`ResolutionFactor`、`ScaleAvatar`、`ScaleFrameOffsets`、`Enabled`；构造时拒绝空路径或小于等于 0 的倍率。静态 `XiaHighResolutionTextureRegistry` 提供：

```csharp
public static void Register(string pPath, float pResolutionFactor = 4f,
    bool pScaleAvatar = true, bool pScaleFrameOffsets = true,
    bool pEnabled = true);
public static bool SetEnabled(string pPath, bool pEnabled);
public static bool TryGet(string pPath,
    out XiaHighResolutionTextureProfile pProfile);
public static bool IsEnabled(string pPath);
public static float ResolveFactor(string pPath, float pFallback);
public static bool TryGetAvatarProfile(string pAssetId, bool pIsKing,
    bool pIsBaby, out XiaHighResolutionTextureProfile pProfile);
public static string NormalizePath(string pPath);
```

使用 `Dictionary<string, XiaHighResolutionTextureProfile>`、`StringComparer.Ordinal` 和写锁/读取快照；`NormalizePath` 把 `\` 转 `/` 并去掉首尾 `/`。类型初始化登记 `actors/species/civs/Xia/king` 与 `actors/species/civs/Xia/king_han`，倍率均为 4。重复注册覆盖旧值；`TryGetAvatarProfile` 只为成年 Xia 国王返回默认 `king` profile。

- [ ] **Step 2: 让旧规则类委托登记表**

保留 `BodyResolutionFactor`、头像偏移常量和旧版 `ResolveAvatarScale(float,float)` 签名；`IsHighResolutionTexturePath` 委托 `Registry.IsEnabled`；新增 `ResolveAvatarScale(float,float,float)`，旧签名传入 `BodyResolutionFactor`，供补丁使用任意 profile 倍率。

- [ ] **Step 3: 编译并提交**

运行 `dotnet build AncientWarfare3.csproj --no-restore`，预期 0 warnings、0 errors；随后：

```powershell
git add -- Code/core/presentation/XiaHighResolutionTextureRegistry.cs Code/core/presentation/XiaKingScaleRules.cs
git commit -m "Add centralized Xia high-resolution texture registry"
```

### Task 2: 先写登记表规则测试

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/XiaHighResolutionTextureRegistryTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: 编写失败测试**

`Run()` 断言：反斜杠和首尾分隔符能命中 `king_han`、倍率为 4；未知 `leader_1` 未启用；未登记路径不能 `SetEnabled`；禁用/恢复 `king_han` 生效；重复注册 `test` 更新倍率和开关；未知路径使用 fallback；非 Xia、幼年国王不返回头像 profile，成年 Xia 国王返回 profile。另用 `ExpectThrows` 验证空路径和 0 倍率抛出 `ArgumentException`。测试工程链接新 registry 源文件。

- [ ] **Step 2: 运行测试确认先失败，再实现后重跑**

运行 `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`。若既有 source guard 在编译前阻断，记录具体 guard；生产编译仍须通过。完成 Task 1 后重跑，新增断言应通过。

- [ ] **Step 3: 提交测试**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/XiaHighResolutionTextureRegistryTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "Test Xia high-resolution texture registry switching"
```

### Task 3: 让动画容器按登记项应用倍率

**Files:**
- Modify: `Code/patch/AW_HighResolutionSpritePatch.cs:29-115`

- [ ] **Step 1: 将身体缓存改为 Sprite→倍率映射**

把 `HashSet<Sprite>` 改为 `Dictionary<Sprite,float>`。容器创建后调用 `Registry.TryGet(pTexturePath, out profile)`，仅处理启用且倍率大于 1 的 profile；注册每个身体 Sprite 对应倍率。

- [ ] **Step 2: 参数化帧偏移换算**

将 `ScaleDownFrameOffsets` 接收 `float pFactor`；只有 `ScaleFrameOffsets` 为真时调用，用 profile 倍率替换固定 4，保留现有手持物锚点修正。

- [ ] **Step 3: 按 Sprite 倍率重建最终 Sprite**

`CreateFinalSpritePostfix` 从字典读取倍率；没有映射、倍率不大于 1 或 rect 无效时返回；以该倍率作为 `pixelsPerUnit` 创建新 Sprite，避免不同目录互相套用固定 4×。

- [ ] **Step 4: 编译、检查并提交**

运行 `dotnet build AncientWarfare3.csproj --no-restore` 和 `git diff --check`，预期构建无警告错误、diff 检查返回 0；提交：

```powershell
git add -- Code/patch/AW_HighResolutionSpritePatch.cs
git commit -m "Drive Xia animation scaling from texture registry"
```

### Task 4: 让头像面板复用同一 profile

**Files:**
- Modify: `Code/patch/AW_XiaKingScalePatch.cs:35-105`

- [ ] **Step 1: 替换固定国王判断**

新增 `TryGetAvatarProfile(UnitAvatarLoader,out XiaHighResolutionTextureProfile)`，读取 `ActorAvatarData` 并调用 registry 的 `TryGetAvatarProfile`；profile 缺失、禁用或 `ScaleAvatar=false` 时保持原版返回。

- [ ] **Step 2: 使用 profile 倍率计算头像和手持物**

`LoadAvatarPostfix` 调用三参数 `ResolveAvatarScale`；`SetImageParamsPostfix` 用 profile 倍率乘 anchored position 与 item sizeDelta。边框仍按计算后的 scale 绝对赋值，避免池化对象重复缩放。

- [ ] **Step 3: 编译、部署路径核对并提交**

运行生产构建和 `git diff --check`；用 `Get-FileHash` 比较目标目录 registry 源码与工作区，并确认没有复制 `Assemblies/AncientWarfare3.dll`。提交：

```powershell
git add -- Code/patch/AW_XiaKingScalePatch.cs Code/core/presentation/XiaKingScaleRules.cs
git commit -m "Use registry profile for Xia avatar scaling"
```

### Task 5: 集成验证、源码部署与推送

**Files:**
- Verify: `deploy-local.ps1`
- Verify: `Code/core/presentation/XiaHighResolutionTextureRegistry.cs`
- Verify: `Code/patch/AW_HighResolutionSpritePatch.cs`
- Verify: `Code/patch/AW_XiaKingScalePatch.cs`

- [ ] **Step 1: 运行完整生产构建**

运行 `dotnet build AncientWarfare3.csproj --no-restore`，预期 0 warnings、0 errors；规则测试若被既有 source guard 阻断，记录 guard 名称且不修改无关 guard。

- [ ] **Step 2: 仅部署源码**

运行 `./deploy-local.ps1`，预期输出 `DEPLOY-DONE` 与 `Preserved: Assemblies (runtime dependencies only)`；目标目录保留运行时依赖但没有本项目生成的 `Assemblies/AncientWarfare3.dll`。

- [ ] **Step 3: 重启并检查 Player.log**

重启 WorldBox，等待 NML 编译；确认 `Player.log` 没有 `error CS` 或 `Failed to compile mod Ancient Warfare 3`，并出现 `Compile Mod Ancient Warfare 3`；确认进程 `Responding=True`。

- [ ] **Step 4: 推送并核对工作区**

```powershell
git push
git status -sb
```

预期远程推送成功、工作区干净、分支与 `origin/b/20260822-baseline-non-path-port` 同步。
