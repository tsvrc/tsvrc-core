# Testing Strategy for Tsvrc (UdonSharp / VRChat SDK3)

Research date: 2026-07-03. Updated 2026-07-04 for the package restructure into
`Runtime/` (single `Tsvrc.Runtime.asmdef`) and `Editor/` (single `Tsvrc.Editor.asmdef`,
with generator code under `Editor/CodeGen`). Sources at the bottom.

## Current status

Base structure implemented:
- `Tests/Editor/Tsvrc.Tests.Editor.asmdef` — Edit Mode test assembly (Editor-only platform).
- `Tests/PlayMode/Tsvrc.Tests.PlayMode.asmdef` — Play Mode test assembly.
- `com.unity.test-framework` is already a project dependency (`Packages/manifest.json`) — no package install needed.
- NSubstitute installed via NuGetForUnity (`Assets/Packages/NSubstitute.5.3.0/`). Both test
  asmdefs reference `NSubstitute.dll` via `precompileReferences` and gate a `NSUBSTITUTE`
  compilation symbol via `versionDefines` (auto-defined only in assemblies where the DLL is
  present — no global Scripting Define Symbol needed). Guard usage with `#if NSUBSTITUTE`.

All of `Runtime/` now compiles as a single `Tsvrc.Runtime.asmdef`, referenced by both
test asmdefs. This supersedes the earlier `TsArray`-only
`Tsvrc.Utils.Pure.asmdef` proof of concept described in prior revisions of this doc — that
asmdef has been deleted and `TsArray.cs` folded back into `Runtime/Utils/`, since every class
under `Runtime/` is now referenceable from tests through the one `Tsvrc.Runtime` assembly.

**One step that still can't be automated:** UdonSharp requires a companion **"U#
Assembly Definition"** asset alongside any `.asmdef` that contains code called from Udon,
which is now true of `Tsvrc.Runtime.asmdef` as a whole (it contains every
`UdonSharpBehaviour` in the library). That companion asset is a ScriptableObject created
and wired up through Unity's own menu, not a plain text file. **Do this once in the
Editor:**
1. Right-click the `Runtime/` folder > **Create > U# Assembly Definition**.
2. Name it identically to the existing one: `Tsvrc.Runtime`.
3. In the Inspector, set its **Source Assembly** field to `Tsvrc.Runtime.asmdef`.
4. If UdonSharp still complains scripts aren't part of a U# assembly, reimport the
   `Runtime/` folder (right-click > Reimport).

### Manual steps still needed from you

1. **Open the project in Unity** and do the U# Assembly Definition step above.
   Confirm the project compiles and every `UdonSharpBehaviour` in `Runtime/` still
   compiles to Udon without errors.
2. **Install ClientSim** (`com.vrchat.clientsim`) via VRChat Creator Companion (VCC) —
   not installed in this project yet (checked `Packages/vpm-manifest.json`). This is a
   GUI action in VCC, not something to hand-edit into the manifest.
3. **Disable Domain Reload** for faster/more reliable Play Mode test runs later:
   Edit > Project Settings > Editor > Enter Play Mode Settings > uncheck "Reload
   Domain". Optional, but recommended once Play Mode tests exist.
4. ~~NSubstitute / NuGetForUnity~~ — done. Installed and wired into both test asmdefs.

## TL;DR

There is **no official VRChat/UdonSharp unit-testing framework**. But testing is
still very achievable because of one key fact:

> **In Unity Editor Play Mode, UdonSharpBehaviours run as their plain C# "proxy"
> class, not as compiled Udon assembly.** Compilation to Udon bytecode only
> happens for the actual build/upload. This means Unity's official
> **Unity Test Framework (UTF / NUnit)** can exercise real UdonSharp logic in
> both Edit Mode and Play Mode without VRChat running at all.

So the practical stack is a layered pyramid, official tooling first, VRChat-specific
simulators only where needed:

| Layer | Tool | What it catches |
|---|---|---|
| 1. Pure logic unit tests | **Unity Test Framework (Edit Mode, NUnit)** | Algorithms, data structures, parsing, math — anything not touching `UdonSharpBehaviour`/scene objects |
| 2. Behaviour/integration tests | **Unity Test Framework (Play Mode, NUnit + `UnityTest` coroutines)** | UdonSharpBehaviour proxy logic, event wiring, component interactions, GameObject lifecycle |
| 3. VRChat API simulation | **ClientSim** (official, `com.vrchat.clientsim`) | Player join/leave, ownership, ClientSim's ExampleTests-style automated interaction tests |
| 4. Networked / multi-client | **VRChat SDK "Build & Test"** (multiple local clients) | Real networking, sync vars, ownership transfer, RPCs, actual Udon VM behavior |
| 5. Mocking VRChat-only APIs | **Guribo/UdonUtils test-mode pattern + NSubstitute** | Substituting `VRCPlayerApi`/Networking calls that can't run outside the client |

None of these existed as a single "VRChat testing framework" — this is an assembled
stack from Unity's official test runner plus two community VRChat-specific tools
(ClientSim is official/VRChat-maintained; UdonUtils is community).

## Layer 1 & 2: Unity Test Framework (do this first)

This is Unity's built-in package (`com.unity.test-framework`), NUnit-based, accessed
via **Window > General > Test Runner**. It is the same framework used for any Unity
project and needs no VRChat-specific setup.

- **Edit Mode tests**: run in-editor without entering Play Mode. Fast, no scene
  needed. Best for anything you can factor out as plain C# (no `MonoBehaviour`
  lifecycle, no scene refs). Great fit for things like `Runtime/Utils`
  (`TsArray.cs`, `TsJson.cs`, `TsMemory.cs`), `DataChunker.cs`, `TsvrcTranslationConfig.cs`,
  and the `Editor/CodeGen` modules — all of these are logic-heavy and don't need
  a running scene.
- **Play Mode tests**: run inside Play Mode, can use `[UnityTest]` + `IEnumerator` to
  wait frames. Since UdonSharpBehaviours run as their C# proxy in-editor, you can:
  1. Instantiate a prefab/GameObject with your `UdonSharpBehaviour` in a test scene.
  2. Call its public methods / trigger its Unity events directly.
  3. Assert on resulting state.
- **Requirement**: create an **Assembly Definition** for tests (`.asmdef`), referencing
  `nunit.framework.dll`, with `Editor`-only platform for Edit Mode tests, or normal
  platforms + "Test Assemblies" checked for Play Mode.
- **Known gotcha**: ClientSim/UdonSharp play-mode tests must run with **Domain Reload
  disabled** (Edit > Project Settings > Editor > "Enter Play Mode Settings" — disable
  Reload Domain), otherwise static/singleton state gets wiped between test runs and
  proxies can lose references.

### Suggested folder layout
```
Tests/
  Editor/
    Tsvrc.Tests.Editor.asmdef   (references nunit.framework.dll, platform=Editor)
    Utils/TsArrayTests.cs
    Utils/TsJsonTests.cs
    DataTransfer/DataChunkerTests.cs
  PlayMode/
    Tsvrc.Tests.PlayMode.asmdef (Test Assemblies checked, no platform restriction)
    Core/TsvrcInstanceTests.cs
    StateMachine/StateManagerTests.cs
```

## Layer 3: ClientSim (`com.vrchat.clientsim`)

Official VRChat package, simulates the VRChat client inside the Unity Editor
(player spawn, interact events, pickups, respawn, some networking semantics)
without needing to launch the real client. Install via VRChat Creator Companion (VCC).

- Docs mention a built-in **"Automated Testing"** feature that must be explicitly
  enabled (opt-in, won't affect the project otherwise). As of this research its
  detailed documentation is still marked "forthcoming" upstream, but the mechanism
  exists in the package and is usable with Unity's Test Runner (Play Mode) today —
  worth inspecting the ClientSim repo's own `Tests/` folder for the pattern it uses
  internally, since it dogfoods its own automated test harness there.
- Best used for scripts that touch `Networking.LocalPlayer`, `VRCPlayerApi`,
  interact/pickup events — e.g. `AutoPlayerTracker.cs`, `PlayerTracker.cs`,
  `TsPlayer.cs`, `HeadClipGuard.cs`.
- Known limitation (tracked upstream): UdonSharpBehaviour references can be missing
  in Play Mode tests depending on domain-reload/init order — same mitigation as
  above (disable domain reload for the test session).

## Layer 4: VRChat SDK "Build & Test"

The only way to test **real compiled Udon bytecode** and **actual networking**
(SyncVars, ownership transfer, custom network events) across multiple simulated
players. Not automatable as classic CI unit tests (it launches real VRChat client
processes), but:

- Setting "Number of Clients" to 0 turns it into **"Build & Reload"** — fast
  iteration without relaunching VRChat.
- The SDK exposes a public build-pipeline API (`VRCSdkControlPanel.TryGetBuilder<T>`,
  `OnSdkBuildStart`, `OnSdkBuildEnd`, `OnSdkPanelEnable`) that tooling can hook for
  custom pre-build validation — useful for **scripted sanity checks** (e.g. verifying
  Tsvrc's `CodeGen`/`TsvrcBuildCompile.cs` output is consistent) even though it's
  not a "unit test" mechanism per se.
- Treat this layer as your **manual/scripted smoke-test gate before publishing**, not
  as part of a fast feedback loop.

## Layer 5: Mocking VRChat-only APIs (Guribo/UdonUtils pattern)

[Guribo/UdonUtils](https://github.com/Guribo/UdonUtils) is a community UdonSharp
utility library that ships its own test-mode convention, useful as a reference
pattern to copy (not necessarily to depend on):

- A compilation symbol (`TLP_UNIT_TESTING`) gates workarounds for VRChat SDK pieces
  that can't be mocked normally, so production builds are unaffected.
- Optional **NSubstitute** (via NuGetForUnity, symbol `NSUBSTITUTE`) is used to create
  substitute/mock objects for interfaces around VRChat APIs. If NSubstitute isn't
  installed, dependent tests are skipped rather than failing to compile.
- Applicable pattern for Tsvrc: wrap direct calls to `Networking.*` / `VRCPlayerApi`
  behind small interfaces in the `Core`/`Network` folders so Edit/Play Mode tests can
  substitute them, reserving ClientSim/Build&Test only for the parts that
  genuinely need real VRChat behavior.

## Recommended adoption plan for Tsvrc

1. **Add the Unity Test Framework package** (usually already present) and create the
   `Tests/Editor` + `Tests/PlayMode` asmdefs described above.
2. **Extract pure logic** out of long `UdonSharpBehaviour` classes into plain C#
   helper classes/structs (no scene/component dependency) wherever feasible — these
   become trivially Edit-Mode-testable and are the highest ROI given the "classes are
   too long" pain point. Good first candidates: `TsvrcInstance.cs`, `StateManager.cs`,
   `Runtime/DataTransfer/*`, `TsvrcTimer.cs`.
3. **Write Play Mode tests** for the remaining `UdonSharpBehaviour` glue, driving it
   through its public API/Unity events, relying on the proxy-runs-as-C# behavior.
4. **Introduce ClientSim** for player/networking-adjacent scripts once (1)-(3) are in
   place; enable its automated testing opt-in and iterate against its own `Tests/`
   examples in the GitHub repo.
5. **Keep Build & Test / real client testing** as the final pre-publish gate for
   sync-heavy features (this cannot be replaced by any editor-level test).
6. Optionally adopt the `TLP_UNIT_TESTING` + NSubstitute pattern from UdonUtils if/when
   you need to mock `Networking`/`VRCPlayerApi` calls directly inside tests rather than
   routing through ClientSim.

## Sources

- [UdonSharp docs](https://udonsharp.docs.vrchat.com/)
- [UdonSharp Community Resources](https://udonsharp.docs.vrchat.com/community-resources/)
- [ClientSim docs](https://clientsim.docs.vrchat.com/)
- [ClientSim GitHub](https://github.com/vrchat-community/ClientSim)
- [ClientSim: Missing UdonSharpBehaviour References in Play-Mode Tests (issue #63)](https://github.com/vrchat-community/ClientSim/issues/63)
- [Guribo/UdonUtils GitHub](https://github.com/Guribo/UdonUtils)
- [VRChat Creation: Using Build & Test](https://creators.vrchat.com/worlds/udon/using-build-test/)
- [VRChat Creation: UdonSharp](https://creators.vrchat.com/worlds/udon/udonsharp/)
- [Unity Test Framework docs (Edit Mode vs Play Mode tests)](https://docs.unity3d.com/Packages/com.unity.test-framework@1.0/manual/edit-mode-vs-play-mode-tests.html)
