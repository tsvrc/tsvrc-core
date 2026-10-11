# Contributing to TsVRC Core

Bug reports, fixes and new features are welcome. Before you build a feature or change a public API,
open an issue to agree on its shape, so the code you write is the code that gets merged. Questions
go in [GitHub Discussions](https://github.com/orgs/tsvrc/discussions).

## Reporting a bug

Use the bug report form. Say which Core and VRChat Worlds SDK versions you use. Steps that start
from a new world project are the most useful thing you can attach.

## Setup

Core is a VPM package, so it needs a world project to develop in.

1. Create a world project with Unity 2022.3 and VRChat Worlds SDK 3.10, through the
   [VRChat Creator Companion](https://vcc.docs.vrchat.com/).
2. Clone this repository into the `Assets` folder of that project as `Assets/Tsvrc`, with its
   submodule:

   ```powershell
   git clone --recurse-submodules https://github.com/tsvrc/tsvrc-core.git Assets/Tsvrc
   ```

   In a clone made without `--recurse-submodules`, run `git submodule update --init` once.
3. Open the project in Unity.

The submodule is [Udon Test Kit](https://tsvrc.com/docs/udon-test-kit/intro), at
`Tests/UdonTestKit`, pinned to the kit release Core is tested with. The Core editor code and tests
build on it, and a Core release lists it as a dependency instead of shipping it. Don't install the
kit through the Creator Companion as well: two copies stop the project compiling.

## Running tests

The tests are in `Tests/EditMode` and `Tests/PlayMode`, with the test doubles both use in
`Tests/Doubles`. They're written with Udon Test Kit, whose
[documentation](https://tsvrc.com/docs/udon-test-kit/intro) covers `ClientSimTestBase`, `Session`,
`CallLog` and the rest.

Run both modes before opening a pull request. In the Editor, use the EditMode and PlayMode tabs of
**Window > General > Test Runner**. If a Play Mode test fails before ClientSim starts, with a
message naming project settings, run **Tools > TsVRC > Udon Test Kit > Fix ClientSim Project
Settings** once. From PowerShell, with the Editor closed:

```powershell
.\Assets\Tsvrc\Tests\UdonTestKit\Scripts~\Invoke-UnityTests.ps1 -AssemblyNames "Tsvrc.Tests.EditMode;Tsvrc.Tests.PlayMode"
```

Without `-AssemblyNames` the script runs every test assembly under `Assets`, your world tests
included. Its options are in the
[Invoke-UnityTests.ps1 reference](https://tsvrc.com/docs/udon-test-kit/reference/invoke-unity-tests).

## Coding conventions

Match the style of the surrounding file. Two things aren't obvious from reading one file:

- Fields are private. When something needs to be public, expose it as a global instead of
  widening the access of the field.
- Generated code is never edited by hand. When the output looks wrong, the fix belongs in
  `Editor/CodeGen`.

A change to runtime or codegen behavior needs a test that fails without it.

## Documentation

The Core documentation is on [tsvrc.com](https://tsvrc.com/docs/core/intro), built from
`docs/core/` in the [tsvrc/docs](https://github.com/tsvrc/docs) repository. A change to what users
call or see needs a matching pull request there.

## Submitting a pull request

1. Sign off every commit (`git commit -s`) per the
   [Developer Certificate of Origin](https://developercertificate.org/). It certifies you wrote the
   change, or otherwise have the right to submit it under the Apache-2.0 license, so only you can
   add it, never a tool. A check fails the pull request if any of its commits isn't signed off.
2. If AI tools helped, say which parts and how in the pull request description. You're the author
   either way: read, understand and test all of it before you submit it, and be ready to explain it
   in review. A pull request that's mostly unreviewed AI output, and would take longer to review
   than it saves, will be closed.
3. Open the pull request, describing what it changes and why. Community conduct is covered by the
   [org-wide Code of Conduct](https://github.com/tsvrc/.github/blob/main/CODE_OF_CONDUCT.md).

## Reporting a security vulnerability

Don't open a public issue for this. See [SECURITY.md](SECURITY.md).
