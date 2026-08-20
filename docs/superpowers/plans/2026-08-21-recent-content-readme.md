# Recent Content README Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish an accurate player-facing introduction to playable changes merged from 2026-08-18 through 2026-08-21 and link it from the repository README.

**Architecture:** A standalone root Markdown article owns the detailed update narrative. The existing root README receives only a compact latest-content callout and relative link, avoiding duplicated maintenance.

**Tech Stack:** Git history, GitHub-flavored Markdown, PowerShell verification, Git.

---

### Task 1: Build The Playable Content Inventory

**Files:**
- Read: Git commits dated `2026-08-18` through `2026-08-21`
- Read: representative runtime diffs under `Code/`, `Locales/`, and `EmbededResources/`

- [ ] **Step 1: List runtime-oriented commits**

Run:

```powershell
git log --since="2026-08-18 00:00:00 +0800" --until="2026-08-21 23:59:59 +0800" --no-merges --date=short --pretty=format:"%h`t%ad`t%s"
```

Expected: commits for court, rebellion, restoration, de jure war, migration,
map modes, RTS transport, and performance work.

- [ ] **Step 2: Exclude non-playable claims**

Treat `docs:`, `test:`, `chore:`, and merge-only commits as supporting evidence.
Only describe a feature as playable when a corresponding `feat:`, `fix:`, or
`perf:` runtime commit exists in the date range.

### Task 2: Write The Standalone Player Introduction

**Files:**
- Create: `NEW_CONTENT_2026-08-21.md`

- [ ] **Step 1: Add the title and scope**

Use the literal title:

```markdown
# Ancient Warfare 3.0：四日大型内容更新导览

> 覆盖提交时间：2026 年 8 月 18 日至 8 月 21 日
```

- [ ] **Step 2: Group changes by player workflow**

Write concise sections for court and local government; corruption and
uprisings; restoration and historical figures; de jure warfare; migration and
cultural drift; lineages and map modes; RTS and naval transport; performance
and stability. Each section must state both what changed and what players see.

- [ ] **Step 3: Add a scope note**

End with a note that the article consolidates runtime changes from the date
range and omits design-only or test-only work.

### Task 3: Add The Root README Entry

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a latest-content callout near the top**

Insert a short section after the project introduction:

```markdown
## 最新开发内容

2026 年 8 月 18 日至 21 日完成了朝廷与地方官署、腐败与起义、法理州战争、战争移民、世家地图模式及 RTS 运输等集中更新。

[查看四日大型内容更新导览](NEW_CONTENT_2026-08-21.md)
```

Do not duplicate the full article in `README.md`.

### Task 4: Verify And Publish

**Files:**
- Verify: `README.md`
- Verify: `NEW_CONTENT_2026-08-21.md`

- [ ] **Step 1: Validate files and links**

Run:

```powershell
Test-Path README.md
Test-Path NEW_CONTENT_2026-08-21.md
rg -n "NEW_CONTENT_2026-08-21.md|2026 年 8 月 18 日至 8 月 21 日" README.md NEW_CONTENT_2026-08-21.md
git diff --check
```

Expected: both paths return `True`, the relative link and date scope are found,
and `git diff --check` exits successfully.

- [ ] **Step 2: Commit and push**

Run:

```powershell
git add README.md NEW_CONTENT_2026-08-21.md docs/superpowers/plans/2026-08-21-recent-content-readme.md
git commit -m "docs: publish recent content introduction"
git push origin master
```

Expected: `origin/master` advances to the new documentation commit.
