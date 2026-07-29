# RTS Expanded Battle Speech Design

## Goal

Expand AW3's low-frequency RTS speech bubbles from seven lines to a curated
pool of 24 recognizable *Three Kingdoms (2010)* lines and internet meme
versions. Lines must fit the current bubble and match the army's active battle
context.

## Scope

This feature changes presentation only. It does not alter RTS strategy,
movement, morale, combat damage, war score, or war exhaustion.

Speech remains restricted to battle-related activity:

- an exact assault mission;
- an exact retreat mission;
- an RTS pursuit;
- a defensive captain who is actually in combat;
- an ordinary military actor who is actually in combat.

Rally, march, deploy, regroup, replenish, idle patrol, and other non-combat
states do not become eligible merely to show a line.

## Curated Lines

The pool contains exactly these 24 Chinese lines:

1. `战至最后一刻，自刎归天`
2. `我的大斧早就饥渴难耐`
3. `不要愤怒，愤怒会降低我的智慧`
4. `不可能，绝对不可能`
5. `他过江，我也过江`
6. `胜兵必骄，骄兵必败`
7. `我是不会客气的`
8. `我二弟天下无敌`
9. `徐州城不愧是中原第一雄关`
10. `叉出去`
11. `我看你的头`
12. `曹贼！奸贼！恶贼！逆贼！`
13. `堂堂吕布，为何成了三姓家奴`
14. `吕布有四个爹`
15. `国贼董卓嘛`
16. `生死不明，那就是死了`
17. `我看你是舍不得这张帅案吧`
18. `此战将决定今后五百年的历史`
19. `已经有了一次伏击，就绝不会再有第二次伏击`
20. `他马上会变出十万精兵来`
21. `徐晃是我的韩信、白起、周亚夫`
22. `我部悍将刘三刀，三刀之内必斩吕布`
23. `我有上将潘凤，可斩华雄`
24. `风从虎，云从龙，龙虎英雄傲苍穹`

Line 2 corrects the current `早已` wording to the recognized `早就` meme.
Every line owns one stable localization key. The `cz` and `ch` columns use
the same Chinese text; `en` uses a concise translation that fits the bubble.

## Battle Contexts

The presentation rules expose six contexts:

- `Assault`: exact attack proposal, assault role, and assault state.
- `Defense`: defense proposal or defense role while the actor is in combat.
- `Pursuit`: RTS pursuit state.
- `Retreat`: retreat state or retreat proposal.
- `CrossingWater`: an otherwise eligible army has an active voyage, embarked
  members, or the speaking actor is inside a boat.
- `GeneralCombat`: an eligible fighting soldier without a more specific state.

Context priority is `Retreat`, `CrossingWater`, `Pursuit`, `Defense`,
`Assault`, then `GeneralCombat`.

## Context Pools

The same line may belong to multiple compatible pools. Each pool contains at
least two lines except `CrossingWater`, because `他过江，我也过江` is the only
approved water-specific line.

### Assault

- 战至最后一刻，自刎归天
- 我的大斧早就饥渴难耐
- 我是不会客气的
- 徐州城不愧是中原第一雄关
- 此战将决定今后五百年的历史
- 他马上会变出十万精兵来
- 我部悍将刘三刀，三刀之内必斩吕布
- 我有上将潘凤，可斩华雄
- 风从虎，云从龙，龙虎英雄傲苍穹

### Defense

- 战至最后一刻，自刎归天
- 我二弟天下无敌
- 胜兵必骄，骄兵必败
- 我看你是舍不得这张帅案吧
- 已经有了一次伏击，就绝不会再有第二次伏击
- 徐晃是我的韩信、白起、周亚夫

### Pursuit

- 叉出去
- 我看你的头
- 曹贼！奸贼！恶贼！逆贼！
- 堂堂吕布，为何成了三姓家奴
- 吕布有四个爹
- 国贼董卓嘛
- 我是不会客气的

### Retreat

- 不可能，绝对不可能
- 不要愤怒，愤怒会降低我的智慧
- 胜兵必骄，骄兵必败
- 生死不明，那就是死了
- 已经有了一次伏击，就绝不会再有第二次伏击

### Crossing Water

- 他过江，我也过江

### General Combat

- 战至最后一刻，自刎归天
- 我的大斧早就饥渴难耐
- 我二弟天下无敌
- 曹贼！奸贼！恶贼！逆贼！
- 堂堂吕布，为何成了三姓家奴
- 国贼董卓嘛
- 生死不明，那就是死了
- 我部悍将刘三刀，三刀之内必斩吕布
- 我有上将潘凤，可斩华雄
- 风从虎，云从龙，龙虎英雄傲苍穹

## Selection And Frequency

The existing active-play timing remains unchanged:

- same actor cooldown: 20 active game seconds;
- global interval: 3 active game seconds;
- maximum simultaneous bubbles: 2;
- display duration: 3 active game seconds;
- paused worlds do not advance lifetime, scan, or cooldown state.

Selection is uniform within the chosen context pool. If a pool contains more
than one line, the immediately previous displayed line is excluded. The
selected formatted text is stored on the active bubble and cannot change
while that bubble is visible.

## Text Layout

The bubble sprite, position, and size remain unchanged. Formatting adapts to
the larger pool:

- Chinese text wraps at punctuation where possible and otherwise at eight
  CJK characters per line;
- Chinese text may use up to four lines and is never silently truncated;
- Chinese text up to 10 characters uses scale `1.4`, 11-16 characters uses
  scale `1.2`, and longer text uses scale `1.0`;
- the existing English word wrapping remains capped at four lines.

## Failure Handling

A missing localization uses that line's built-in English fallback. A line
with neither localized text nor fallback is not displayed. Stale actors,
world teardown, draw failures, and the stock `talk_bubbles` option keep their
current guards.

## Verification

Automated rule tests cover:

- the six contexts and their priority;
- captain and ordinary-soldier eligibility boundaries;
- exact pool sizes and representative membership/exclusion;
- all 24 stable localization keys;
- immediate-repeat avoidance;
- CJK wrapping and adaptive scale boundaries;
- unchanged pause-aware frequency constants.

Source guards require all 24 CSV rows, matching `cz`/`ch` text, non-empty
English text, and per-bubble stored text. Final verification runs the focused
bubble slice, complete Rules Tests, both bubble source guards, a Release
build, deployment hash comparison, and in-game checks for assault, defense,
pursuit, retreat, crossing-water, and paused bubbles.
