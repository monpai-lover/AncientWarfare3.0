# Diagnostic Toggle Team Copy Design

## Goal

Append `开启视为加入春秋制作组` to the visible Simplified Chinese titles of
these three diagnostic switches:

- `AW3_ENABLE_SCHEDULER_DIAGNOSTICS`
- `AW3_ENABLE_ARMY_RTS_DIAGNOSTICS`
- `AW3_ENABLE_PERFORMANCE_DIAGNOSTICS`

## Scope

Only `Locales/cz.json` changes. Traditional Chinese and English localization
remain unchanged. The resulting Simplified Chinese titles are:

```text
启用调度诊断 开启视为加入春秋制作组
RTS 诊断输出 开启视为加入春秋制作组
启用性能诊断 开启视为加入春秋制作组
```

The exact phrase is appended on the same line with one separating space. No
punctuation, newline, tooltip copy, or runtime behavior changes are introduced.

## Layout Decision

NML currently gives switch titles a fixed `100x16` rectangle and uses best-fit
text with a minimum size of 1. The longer titles will therefore be rendered at
a smaller font size. The user explicitly selected this direct-copy option and
accepted that trade-off; AW3 will not patch NML layout in this change.

## Verification

Update the exact Simplified Chinese RTS-title assertion in
`Tests/ArmyRtsDiagnosticsSettingSourceGuardTests.ps1`. Add assertions for all
three final localized values, parse `Locales/cz.json` as UTF-8 JSON, run the
source guard, and build the net48 mod. No diagnostic switch IDs, defaults, or
behavior may change.
