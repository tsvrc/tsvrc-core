# Contributing to TsVRC Core

## Setup

This repo is a VPM package, not a full project on its own (see `package.json`), so you
need a world project to develop it in:

1. Create a new VRChat World project through the [VRChat Creator
   Companion](https://vcc.docs.vrchat.com/), matching **Unity 2022.3** (`package.json`'s
   manifest target).
2. Clone this repo into that project's `Assets` folder as `Assets/Tsvrc`, with its
   submodule:

   ```powershell
   git clone --recurse-submodules https://github.com/tsvrc/tsvrc-core.git Assets/Tsvrc
   ```

   In a clone made without `--recurse-submodules`, run `git submodule update --init` once.
3. Open the project in Unity.

The submodule is [Udon Test Kit](https://tsvrc.com/docs/udon-test-kit/intro), at
`Tests/UdonTestKit`, pinned to the commit Core is tested with. Core's editor code and tests
build on it, and a release lists it as a dependency instead of shipping it. Don't also install
the kit through the Creator Companion in the same project: two copies stop it compiling.

## Running tests

Tests live under `Tests/EditMode` and `Tests/PlayMode`, with the test doubles both use in
`Tests/Doubles`. They're written with Udon Test Kit; its
[documentation](https://tsvrc.com/docs/udon-test-kit/intro) covers `ClientSimTestBase`,
`Session`, `CallLog` and the rest.

### From the Editor

Open **Window > General > Test Runner** and run the EditMode and PlayMode suites from
there.

If a Play Mode test fails before ClientSim starts, with a message naming project settings,
run **Tools > TsVRC > Udon Test Kit > Fix ClientSim Project Settings** once in your project.

### From the command line (Windows)

Close the Unity Editor, then run the kit's script with Core's two test assemblies:

```powershell
.\Assets\Tsvrc\Tests\UdonTestKit\Scripts~\Invoke-UnityTests.ps1 -AssemblyNames "Tsvrc.Tests.EditMode;Tsvrc.Tests.PlayMode"
```

It prints only the tests that didn't pass, with their messages, and a count of the rest, and
exits with 1 when any failed. Without `-AssemblyNames` it runs every test assembly under
`Assets`, your world's included. Its other options are in the
[Invoke-UnityTests.ps1 reference](https://tsvrc.com/docs/udon-test-kit/reference/invoke-unity-tests).

If PowerShell refuses to run it because of its execution policy, allow local scripts once:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
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
