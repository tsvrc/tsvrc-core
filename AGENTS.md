# AGENTS.md

TsVRC Core is an UdonSharp framework for VRChat worlds (Unity 2022.3, VPM package
`com.tsvrc.core`). [CONTRIBUTING.md](CONTRIBUTING.md) covers setup, tests, coding conventions and
pull requests; follow it. This file only adds what it leaves out.

## Layout

- `Runtime/` ships in world builds. `Editor/` holds the editor tools, with the code generator in
  `Editor/CodeGen`.
- `Tests/EditMode/` and `Tests/PlayMode/` hold the tests, with shared test doubles in
  `Tests/Doubles/`. They use Udon Test Kit (`Tsvrc.UdonTestKit`), a submodule at
  `Tests/UdonTestKit`, directly: Core has no testing helpers of its own.
- User documentation isn't in this repo: it's `docs/core/` in `tsvrc/docs`.

## Running tests

Close the Unity Editor, then run
`Tests/UdonTestKit/Scripts~/Invoke-UnityTests.ps1 -AssemblyNames "Tsvrc.Tests.EditMode;Tsvrc.Tests.PlayMode"`.
Add `-TestMode EditMode` or `-TestMode PlayMode` to run one mode.
