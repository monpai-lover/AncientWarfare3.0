# Running the checks

Use .NET SDK 9 and PowerShell 7 from the repository root:

```sh
pwsh -NoProfile -File ./run_all_checks.ps1
```

The runner executes every PowerShell check under `Tests`, every executable
test project, the main rules program's inline assertions and all discoverable
`*Tests.Run()` suites, and the RTS simulation's 32-seed campaign plus standalone
scenarios. Each child exit status and command is recorded in
`TestResults/full-checks/results.json`. Any failure makes the runner exit 1;
failures do not prevent independent checks from running.

`run_my_guards.ps1` and `run_relevant_guards.ps1` preserve historical check
lists, many of whose scripts are absent from this checkout. They now report
those entries as `MISSING` and exit nonzero. Use `run_all_checks.ps1` for the
complete current checkout; do not treat missing historical checks as passes.

For a smaller iteration:

```sh
pwsh -NoProfile -File ./run_all_guards.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests -c Release -- --all-suites
dotnet run --project Tests/ArmyRtsAdversarialSimulation -c Release -- --all --seeds 32 --ticks 10000
```

These projects are executable regression programs, not `dotnet test` projects.
The deployment verifier is a helper; its behavior is tested by
`VerifySourceDeployment.Tests.ps1`. The naming-resource check validates all
bundled data, but explicitly reports the upstream comparison as skipped when
the separate ChineseName checkout is absent.
The actor-texture check also reports when the optional artist overlay is
unavailable; in that case it validates the shipped texture resources and
compatibility copies.

Linux test projects restore official `System.Data.SQLite.Core` 1.0.119 and
`Newtonsoft.Json` 13.0.3 packages as needed. The game uses its bundled Windows
SQLite 1.0.99 assembly, so portable test success alone does not establish
compatibility with the game's older SQLite engine. Production SQL must remain
compatible with that engine. The mod's shipped DLLs remain unchanged.

The bundled engine is SQLite 3.9.2, which does not support modern
`INSERT ... ON CONFLICT ... DO UPDATE` syntax. The synchronous localized-name
writer uses a transaction containing `UPDATE` and, when needed, `INSERT`.
`Tests/AWLocalizedNamePersistence.Isolated.Tests` runs the production writer
against SQLite and checks rowid preservation, dependent rows, and rollback
after failing insert/update triggers. The same production SQL was also checked
with SQLite 3.9.2; Linux package tests alone cannot enforce this version limit.

This command does not build/deploy the complete mod or run WorldBox. Those
checks require the matching game assemblies, publicized Assembly-CSharp,
NeoModLoader, Harmony, and an actual game session. Preserve the README's rule
that the mod DLL is built only for an explicitly requested release workflow.
