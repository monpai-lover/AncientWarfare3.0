# Ancient Warfare 3 v1.1.2 Release Design

## Goal

Publish a clean source-mod release named `Ancient Warfare 3 v1.1.2` from
the current `master` branch. The Git tag is `v1.1.2`, the package is
`AncientWarfare3-v1.1.2.zip`, and `mod.json` reports version `1.1.2`.

## Package Contract

The ZIP contains one root directory named `AncientWarfare3.0`. It remains
an NML source mod and does not contain a compiled `AncientWarfare3.dll`.

The package is rebuilt from a runtime whitelist:

- Directories: `ABPackages`, `Assemblies`, `Code`, `EmbededResources`,
  `GameResources`, `Locales`, `name_generators`, `THIRD_PARTY_NOTICES`, and
  `word_libraries`.
- Root files: `default_config.json`, `icon.png`, `mod.json`, `README.md`,
  `sponsor_qr.jpg`, `supporters.csv`, and `THIRD_PARTY_NOTICES.md`.

The package excludes repository and development content, including `.git`,
`.worktrees`, `.codex`, `.superpowers`, `Tests`, `docs`, `bin`, `obj`,
`release`, `fonts`, project files, handoff notes, logs, databases, caches,
PDB files, nested ZIP files, and any compiled `AncientWarfare3.dll`.

## Cleanup

Packaging uses a temporary staging directory and removes it after ZIP
verification. The ignored local `release/AncientWarfare3-v1.1.0.zip` is
removed only after the new archive passes validation. Existing GitHub
releases and other Git worktrees are not deleted or modified.

## Release Notes

The GitHub release body has four sections:

1. Major updates since `v1.1.0`.
2. Root-cause fixes, with the RTS lifecycle and freeze fixes called out.
3. Verification evidence.
4. Installation instructions stating that the package is an NML source mod
   without a precompiled mod DLL.

The notes cover the fixed war-strength baseline, vanilla tactical handoff,
20/80 withdrawal and synthetic recovery, missionless/wait reconciliation,
empty shared-route recovery, recruitment identity protection, and diagnostic
log throttling. Other user-facing commits since `v1.1.0` are summarized from
Git history rather than omitted.

## Verification And Failure Handling

Before packaging, run the full rules suite, RTS adversarial simulation, and
the net48 mod build. Validate `mod.json` as JSON and confirm its version.

After packaging:

- Open the ZIP and verify the single root directory and required entries.
- Reject forbidden entries and any nested development/generated content.
- Confirm all staged files match their source sizes and compute SHA-256.
- Extract to a temporary directory and validate the install layout.

Create the Git tag and GitHub release only after all checks pass. If upload or
release creation fails, keep the verified local ZIP and report the failure;
do not delete or replace existing remote releases.
