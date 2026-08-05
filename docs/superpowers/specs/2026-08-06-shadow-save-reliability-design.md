# Shadow Diagnostics and Save Reliability Design

## Goal

Make historical persistence reliable when asynchronous shadow diagnostics are enabled, without allowing a save to contain a live/dead lineage mismatch. Add an explicit user-facing explanation for the shadow switch and stop policy-inheritance diagnostics from flooding the log.

## Scope

In scope:

- Historical database worker lifecycle and shadow-mode behavior.
- Actor death archive draining and save-barrier diagnostics.
- Shadow switch tooltip/status text.
- Policy inheritance log de-duplication and rate limiting.
- Focused rule, source-guard, build, and persistence-path tests.

Out of scope:

- Replacing the existing historical database schema.
- Allowing saves with uncommitted death archives.
- Changing non-shadow AI, traversal, army RTS, or UI scheduling behavior.
- Reworking the general logging framework.

## Design

### Shadow and database worker

`EnableAsyncDatabaseWrites` controls whether the historical SQLite worker starts. `EnableAsyncShadowChecks` never disables that worker. When both switches are enabled, the worker performs the real batched SQLite writes while shadow diagnostics compare the expected operation summary with the operation produced by the async path.

The historical write APIs must enqueue valid envelopes in shadow mode and return their normal success/failure result. The old `historical async writer is shadow-only` failure path is removed. Shadow is diagnostic-only and must not be used as a persistence fallback or save gate.

Because `HistoricalSqliteBatchSink` already executes a batch in one transaction, death archives continue through the existing `ActorDeathArchiveService` and `LineageArchiveWriter` queue. The synchronous per-item path remains an error fallback only when the worker cannot accept an operation.

### Save barrier and death archives

The save preparation sequence remains fail-closed:

1. Drain pending in-memory death archives into the historical write queue.
2. Flush the historical worker to its accepted sequence and pump completions.
3. Complete actor archive and family-tree projection callbacks.
4. Require zero pending death archives, a completed historical barrier, and no terminal worker fault.
5. Checkpoint the lineage archive and allow the game save.

The save timeout is not made unbounded and the barrier does not silently discard pending records. Failure diagnostics include the pending actor count, earliest uncommitted sequence, worker error, retry information, and elapsed flush time where available.

### Shadow switch UX

The shadow toggle receives a tooltip and state description:

> Shadow 诊断：仅用于开发/复现异步差异。开启后会额外校验异步结果，可能增加日志和少量开销；不会替代数据库写入。正常游玩请关闭。

The UI distinguishes `关闭：正常异步运行` from `开启：诊断模式，发现差异时记录日志`. The default remains disabled. No save or runtime behavior depends on shadow being enabled.

### Policy inheritance diagnostics

Policy-inheritance diagnostics use a runtime de-duplication key composed of world generation, child kingdom ID, and source kingdom ID (plus the relevant inheritance state when needed). A successful state is logged once per key. Repeated attempts with the same state are suppressed; a state change or new world generation permits a new diagnostic. Suppression is runtime-only and is cleared with the existing policy-inheritance runtime reset.

## Error handling

- Worker unavailable, queue full, SQLite retry exhaustion, terminal fault, or completion callback failure keeps save preparation failed.
- Shadow comparison mismatch logs a structured diagnostic but does not reject an otherwise valid write.
- A failed synchronous fallback never removes the death archive from the pending queue.
- All new diagnostics are bounded in size and avoid logging the same state on every frame.

## Verification

- Rule tests cover shadow worker semantics, save readiness, save error details, and policy log de-duplication keys.
- Source guards verify that shadow mode cannot return `shadow-only` for a valid database write and that the tooltip is registered with the toggle.
- Integration-style tests exercise batched death archive writes with shadow enabled and verify that the save barrier waits for completion.
- Fault-path tests verify that SQLite/worker failures still block saving.
- `dotnet build AncientWarfare3.csproj -c Release` and the existing rule-test runner must pass.

## Acceptance criteria

1. With async database writes and shadow checks enabled, a world with dozens of pending actor deaths can save after the worker commits the batch.
2. No successful write reports `historical async writer is shadow-only`.
3. A real historical write fault still blocks saving and identifies the remaining work.
4. The shadow toggle explains its diagnostic-only purpose and normal-play recommendation.
5. Repeated policy-inheritance events no longer produce unbounded duplicate log lines.
6. Existing behavior is unchanged when shadow checks are disabled.
