# Contributing to TsVRC

## Setup

This repo is a VPM package, not a full project on its own (see `package.json`), so you
need a world project to develop it in:

1. Create a new VRChat World project through the [VRChat Creator
   Companion](https://vcc.docs.vrchat.com/), matching **Unity 2022.3** (`package.json`'s
   manifest target).
2. Clone this repo into that project's `Assets` folder.
3. Open the project in Unity.

## Running tests

Tests live under `Tests/EditMode` and `Tests/PlayMode`, wired into their own assemblies
referencing `Tsvrc.Runtime`/`Tsvrc.Editor`. See [Testing your
world](https://tsvrc.com/docs/tsvrc/testing/testing-your-world) for background on the
testing helpers themselves (`TsPlayModeTestBase`, `PrivateFieldAccess`, ClientSim).

### From the Editor

Open **Window > General > Test Runner** and run the EditMode and PlayMode suites from
there.

### From the command line (Windows)

Windows blocks unsigned scripts by default. If you haven't already allowed local scripts
to run, do this once:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Then:

```powershell
.\Assets\Tsvrc\Scripts~\Invoke-UnityTests.ps1
```

This runs both of TsVRC's own suites (`Tsvrc.Tests.EditMode` / `Tsvrc.Tests.PlayMode`)
and prints a pass/fail line per test, so you don't need the Editor open. It finds your
project by walking up from its own location, and finds the Unity Editor by checking the
version pinned in that project's `ProjectVersion.txt` against Unity Hub's default install
folder, falling back to the registry; if it still can't find one, it asks for the path
instead of failing outright.

| Parameter        | Default                                    | Use it to                                  |
| ----------------- | ------------------------------------------ | ------------------------------------------- |
| `-ProjectPath`     | nearest parent folder with `ProjectSettings` | point at a different Unity project         |
| `-UnityPath`       | auto-detected                              | use a specific Unity install                |
| `-TestMode`        | `All`                                      | run only `EditMode` or only `PlayMode`      |
| `-AssemblyNames`   | `Tsvrc.Tests.EditMode` / `Tsvrc.Tests.PlayMode` | test a different assembly instead      |
| `-ResultsPath`     | a timestamped folder under `$env:TEMP`     | keep the NUnit XML somewhere specific       |

```powershell
.\Assets\Tsvrc\Scripts~\Invoke-UnityTests.ps1 -ProjectPath C:\path\to\your\project -AssemblyNames "YourWorld.Tests.EditMode"
```

Add tests for new behavior; a PR that changes runtime or codegen behavior without a
matching test is unlikely to be merged.

## Coding conventions

Match the style already in the surrounding file. A few conventions worth knowing before
you start:

- Fields are private by default. If something needs to be public, expose it as a global
  rather than widening the field's own access (see recent history on `Editor/CodeGen` for
  examples of this refactor).
- Generated code (anything under a project's configured codegen output folder) is never
  hand-edited; if generated output looks wrong, the fix belongs in `Editor/CodeGen`.

## Submitting a pull request

1. Sign off every commit (`git commit -s`) per the [Developer Certificate of
   Origin](https://developercertificate.org/). This certifies you wrote the change, or
   otherwise have the right to submit it under this project's license (Apache-2.0). PRs
   with unsigned commits will be asked to amend before merge.
2. If AI tooling (Claude, Copilot, or similar) helped write part of your change, say so in
   the PR description, briefly, what it helped with. You're still fully responsible for
   the contribution: read, understand, and test everything before submitting it, the same
   as if you'd written it yourself. A PR that's mostly unreviewed AI output, and would
   take longer to review than it saves, will be closed regardless of disclosure.
3. Fill in the PR template's checklist and open the PR. Community conduct is covered by
   the [org-wide Code of Conduct](https://github.com/tsvrc/.github/blob/main/CODE_OF_CONDUCT.md).

## Reporting a security vulnerability

Don't open a public issue for this. See [SECURITY.md](SECURITY.md).
