# Tsvrc CodeGen Test Suite — Full Coverage Plan

Scope: every file under `Editor/CodeGen/` (`TsvrcGenerator.cs`, `TsvrcModule.cs`,
`UdonWriter.cs`, `PackagePaths.cs`, `TsvrcAssetWatcher.cs`, `TsvrcBuildCompile.cs`,
`TsvrcDomainReloadHandler.cs`, `Config/TsvrcBuiltinConfig.cs`,
`Config/TsvrcTranslationConfig.cs`, and the 8 files under `Modules/`). This is the
"generator" — the piece of the library responsible for turning user-authored config
(scene objects, prefabs, JSON files) into the generated `TsvrcGenerated` partial class
and its scene wiring, with no manual step required. Split out of `TESTING_PLAN.md`
(2026-07-04) because the old Phase 1/6 treatment of this module was a flat checklist;
this file replaces that scope with a from-first-principles enumeration of every
generation path, every validation branch, and every self-healing/idempotency
guarantee the generator makes. `TESTING_PLAN.md` keeps everything else (Runtime/,
Editor/Configure, Editor/Tools) and now just points here for CodeGen.

Extends the harness and conventions already proven out in
`Tests/Editor/CodeGen/*` (reflection harnesses for private statics, `LogAssert.Expect`
for validated warnings/errors, `MethodUnderTest_Scenario_ExpectedResult` naming). See
`TESTING.md` for the general Unity-Test-Framework setup background.

---

## Part 1 — How the generator actually works

Understanding this precisely is the point of this whole exercise: every test below
maps to a specific branch in this description. If a behavior described here isn't
covered by a test in Part 3, that's a gap.

### 1.1 The module contract (`TsvrcModule.cs`)

Every generator module is a `TsvrcModule` subclass, called by `TsvrcGenerator.Run()`
in this fixed sequence, every run:

```
LoadConfig()  →  ExposedFieldNames() / ExcludeFieldNames()  →  GenerateCode()  →
AfterFilesStable()  →  Wire()  →  OnSceneHierarchyChanged() (only on later triggers)
```

- `LoadConfig()` — reads scene (`TsvrcConfig`) and/or asset (`TsvrcBuiltinConfig`,
  `TsvrcTranslationConfig`) sources into private in-memory entries. This is where all
  validation (null/duplicate/wrong-type skip-with-warning) happens.
- `GenerateCode()` — pure function of the entries computed in `LoadConfig()`; returns
  the exact C# source text for the module's generated file, or `null` if the module
  contributes no file. Must be deterministic: same entries in, same string out, byte
  for byte, every time (this is what makes `WriteIfChanged`'s content-diff meaningful).
- `AfterFilesStable()` — runs after `GenerateCode()`'s output has been written and
  (if changed) after `AssetDatabase.Refresh()`'s recompile has completed. Used for
  work that depends on the *compiled* type existing as an asset (program assets) but
  must happen before scene `Wire()` — e.g. creating/normalizing `UdonSharpProgramAsset`s.
  Returning `true` forces another `AssetDatabase.Refresh()` even if no `.cs` changed.
- `Wire()` — mutates the scene: creates/destroys child GameObjects, instantiates
  prefabs, assigns `SerializedObject` properties on the compiled root and on user
  behaviours. Only called when a compiled `TsvrcGenerated` instance already exists in
  the open scene (see `FindRoot()`).
- `OnSceneHierarchyChanged()` — cheap "did my wiring get manually broken" check, used
  to decide whether a rerun should be scheduled after any hierarchy edit. Must be O(1)
  or close to it — called on every hierarchy change while a module is active.
- `ExposedFieldNames()` / `ExcludeFieldNames()` — cross-module field-name collision
  detection. **Only `SingletonModule` currently overrides these** (see §4, "Candidate
  bugs / design gaps" — this makes the cross-module collision path effectively
  untestable through real modules alone).
- `WatchedAssets()` / `WatchedComponentTypeNames()` — declare which asset paths and
  which component type names should trigger `ScheduleRerun()` when they change
  (consumed by `TsvrcAssetWatcher` and `TsvrcGenerator.OnPostprocessModifications`,
  respectively).
- `TabLabel`/`TabDescription`/`DrawTab()` — optional Editor GUI surface, out of scope
  here (covered as generic Editor-tool smoke tests in `TESTING_PLAN.md` Phase 7,
  since `TsvrcWindow`/`ObjectListGUI` own the actual rendering).

Base-class static helpers used across modules: `FindRoot()`, `IsTsvrcBehaviourType()`,
`AliasName()`, `Deduplicate()`, `ApplyAndMarkDirty()`.

### 1.2 Orchestration (`TsvrcGenerator.cs`)

`Run()` is triggered by: domain reload (`TsvrcDomainReloadHandler`), any watched asset
changing (`TsvrcAssetWatcher`), any hierarchy change while modules are active
(`OnHierarchyChanged`), any `SerializedObject` property edit on a watched component
type (`OnPostprocessModifications`), the manual "Force Regenerate" menu item, and a
pre-build hook (`TsvrcBuildCompile`, `skipRefresh: true`).

Per run:
1. Bail out entirely in Play Mode.
2. Unsubscribe both hierarchy/undo handlers (rebuilt at the end only if the run
   completes past the bootstrap gate).
3. **Bootstrap gate**: if not `allowBootstrap` and `HasBootstrapSignal()` is false,
   switch to polling via `WaitForBootstrapSignal` on `hierarchyChanged` and return —
   the generator does nothing to a project that shows no sign of using Tsvrc yet.
4. `CreateModules()` (fixed list, 8 modules) → `LoadConfig()` on each.
5. Cross-module field name collision detection via `ExposedFieldNames()`.
6. Aggregate `WatchedAssets()` (+ each module's own generated file path) into
   `WatchedPaths`.
7. `WriteModules()` — writes each module's `GenerateCode()` output via
   `WriteIfChanged`. If anything changed and `!skipRefresh`: `AssetDatabase.Refresh()`
   and return (recompile will trigger `AfterDomainReload` → `Run()` again).
8. `AfterFilesStable()` on each module (program-asset creation/repair), then
   `WriteModules()` again (state from step 8 can produce more `.cs` changes) — if
   changed *or* `stableChanged` and `!skipRefresh`: `Refresh()` and return.
9. If a compiled `TsvrcGenerated` instance exists in the scene: `Wire()` on each
   module, guarded by `_isWiring`/`_justFinishedWiring` so `Wire()`'s own
   `SerializedObject` writes don't recursively trigger another run via
   `OnPostprocessModifications`.
10. Rebuild `_watchedComponentTypeNames` and re-subscribe both handlers.

**Note (§1.2.a):** step 7's `!skipRefresh` guard only gates the *early return*, not
whether the module writes happened — when `skipRefresh` is true (build-time call) and
files changed, execution falls through to `Wire()` using freshly-recomputed in-memory
`_entries` against the *old* compiled type (no recompile happened). This is presumably
intentional (UdonSharp's own build-time compile pass runs after, at a later callback
order) but is a real, testable, easy-to-get-wrong control-flow subtlety — see §4.

`HasBootstrapSignal()`: true if a scene `TsvrcConfig` exists, OR a compiled
`TsvrcGenerated` instance already exists in-scene, OR any assembly in the AppDomain
declares a non-abstract `TsvrcInstance` subclass (tolerating
`ReflectionTypeLoadException`, using only the types that did load).

`WriteIfChanged()`: skips the write (and thus the reimport it would trigger) when the
existing file's content is byte-identical to the new content — this is the whole
reason `GenerateCode()`'s determinism matters.

### 1.3 Module-by-module generation + wiring logic

- **ScaffoldModule** — must run conceptually first (its generated file declares the
  partial class every other module extends). `GenerateCode()` is a fixed template
  (no config-driven variation at all — the only truly static golden file in the
  suite). `AfterFilesStable()`: creates/repairs the `UdonSharpProgramAsset`, ensures
  exactly one root scene GameObject with the compiled component (destroying
  duplicates), ensures a `TsvrcConfig` child tagged `EditorOnly`, and normalizes
  `UdonSharpProgramAsset.fieldDefinitions` to sorted key order (avoids nondeterministic
  diffs across compiles). `EnsureRootSceneObject()` has three paths: reuse existing
  compiled-type instance(s) (destroying all-but-first), promote a same-named
  GameObject lacking the component, or create a brand new GameObject at sibling index
  0.
- **SingletonModule** — one field per configured singleton (scene `TsvrcConfig` +
  asset `TsvrcBuiltinConfig`, scene first). Name = `AliasName(goName) ?? typeName`,
  except `Animator` which gets `(alias ?? goName) + "Animator"`. Null/duplicate
  entries warn+skip. `_TsSingletonStart()` calls `TsConstruct` only on entries whose
  type actually extends `TsvrcBehaviour` (via `IsTsvrcBehaviourType`). Output ordered
  alphabetically by name (not config order). `Wire()` just assigns direct references,
  no scene object creation.
- **ConstructModule** — one `_construct{Name}` field per `TsvrcConfig.Constructs`
  entry (scene-only source, no builtin equivalent). Same null/duplicate/dedup rules
  as Singleton. `Wire()` warns (doesn't throw) when the field isn't found (stale
  scaffold) or the source is null.
- **FactoryModule** — `Sanitize()` (already unit-tested) turns each `GroupName` +
  prefab name into a PascalCase method-name fragment; `Create{Name}(Transform
  parent)` is generated per entry, branching on whether the prefab is a bare
  `GameObject`, a `TsvrcBehaviour`, or another component type. Entries whose source
  isn't a persistent asset (`EditorUtility.IsPersistent`) are rejected — factories
  only accept prefab assets, not scene objects. `Wire()` instantiates every entry
  under an inactive "Factories" child, **but only if `IsFactoriesAlreadyWired()`
  returns false** — an idempotency check across child count, per-entry prefab
  source identity (`PrefabUtility.GetCorrespondingObjectFromSource`), inactive state,
  and the field's current object reference.
- **PoolModule** — the most complex module. Slot counts come from a dependency graph:
  `ExternalCount` (number of `[WirePool]` fields on *non-pool* scene behaviours
  referencing that type) plus, per pool type, `Σ_parent (fieldCount in parent) ×
  parent.TotalSlots` for every other pool type that itself has `[WirePool]` fields of
  this type — computed via topological DFS (`ComputeForType`) with cycle detection
  (logs an error, treats the cyclic edge as contributing 0, doesn't hang/throw). Types
  with `TotalSlots == 0` are excluded from generation entirely. `Wire()` is two-phase:
  instantiate all slots for all eligible types first (so pool-internal `[WirePool]`
  fields on the newly-created instances become valid wire targets), then re-scan and
  assign every `[WirePool]` field across the *entire* scene (external behaviours
  **and** the just-created pool instances) — logging a mismatch warning (not an
  error) when target count and slot count disagree, in either direction.
  `IsPoolAlreadyWired()` mirrors the same identity checks as Factory's idempotency
  gate, additionally re-validating each `[WirePool]` target's current field value.
- **InstanceModule** — scans the AppDomain once for exactly one non-abstract
  `TsvrcInstance` subclass (deduped by `FullName` to tolerate stale duplicate assembly
  copies). Zero candidates → remove the child + clear the field. Exactly one →
  create-or-repair a single `TsvrcInstance` child (recreating when the component is
  the wrong/stale type, *or* when its backing `UdonSharpProgramAsset` was deleted out
  from under it — checked independently of component presence). More than one → log
  an error and **leave existing wiring completely untouched** (the one case in the
  whole generator where "ambiguous" beats "self-heal").
- **MemoryModule** — always-present: one `_memory` field, auto-creates/heals a
  `TsMemory` scene child (not `EditorOnly` tagged — it's a real runtime object,
  unlike `TsvrcConfig`), assigns the direct reference. Simplest full module in the set
  — good first target once the "temp scene + real root" harness exists.
- **TranslationModule** — `ParseLanguageJson` per configured language `TextAsset`
  (missing `key`/`label`/`entries` → error+skip that file, not the whole run;
  malformed JSON → caught exception, same skip). Enum names built by sanitizing each
  language's `Label`, falling back to `Label_Key` then a numbered suffix on
  collision. Discovers translation targets by scanning **the whole scene** for
  `TextMeshProUGUI` whose GameObject name matches `_key_` **and** is a known
  translation key (so a `_foo_`-named label with no matching JSON key is silently
  excluded, not an error). `_effectiveKeys` diffing (`SyncEffectiveKeys`, shared by
  both `AfterFilesStable` and `OnSceneHierarchyChanged`) avoids rerunning `Wire()`
  when the *set* of matched target names hasn't changed even if individual GameObject
  instances have (e.g. after a hierarchy reorder). Batch translation application at
  runtime is generated with a hardcoded `BatchSize = 20`.

### 1.4 Cross-cutting infrastructure

- **UdonWriter** — indentation/scope bookkeeping only; not config-driven, already
  well covered (Phase 1.1 in `TESTING_PLAN.md`).
- **PackagePaths** — resolves this package's own root via `[CallerFilePath]`, fixed
  at compile time to wherever `PackagePaths.cs` physically lives on disk. Cannot be
  parameterized for tests; assertions here are inherently tied to *this* project's
  actual folder layout (`Assets/Tsvrc`).
- **TsvrcAssetWatcher** — thin `AssetPostprocessor` hook; the interesting logic
  (`AnyMatch`) is a private static pure function.
- **TsvrcDomainReloadHandler** — `[InitializeOnLoad]` static constructor deferring via
  `delayCall`; nothing to unit test beyond "fires exactly once per reload," which is
  really a `TsvrcGenerator.Run()` integration concern.
- **TsvrcBuildCompile** — `IVRCSDKBuildRequestedCallback`; callback order and the
  `Scene`-only / `skipRefresh: true` contract are directly testable by invoking
  `OnBuildRequested` with each `VRCSDKRequestedBuildType`.

---

## Part 2 — Testing strategy for this module specifically

**Nothing in `Editor/CodeGen` needs Play Mode.** Every method above runs purely in
the Editor (most are wrapped `#if UNITY_EDITOR`, several call `EditorApplication`/
`AssetDatabase`/`Undo` APIs directly). All CodeGen tests belong in `Tests/Editor`.
This significantly changes the pyramid shape versus the rest of the library: no
ClientSim tier, no ownership races, no ambiguous ~~ClientSim~~ Test Runner pass/fail
problem (`TESTING_PLAN.md`'s "known environment constraint" section is Play-Mode-only
and does not apply to anything in this document).

### 2.1 Tags used in this document

| Tag | Meaning |
|---|---|
| **CG-Pure** | Static/pure-logic method, zero Unity object creation. Reflection harness where the target is `private`. |
| **CG-Gold** | `GenerateCode()` (or another string-producing method) asserted against an exact expected string, given synthetic entries. |
| **CG-Wire** | Calls `Wire()`/`AfterFilesStable()`/`OnSceneHierarchyChanged()` against a **real but disposable** scene (see 2.2) and asserts resulting scene/asset state. |
| **CG-Orch** | Exercises `TsvrcGenerator.Run()` itself (module sequencing, bootstrap gate, watched-path aggregation, rerun suppression) rather than a single module in isolation. |
| **CG-Smoke** | "Doesn't throw" test for glue code that isn't meaningfully assertable beyond that (`TsvrcDomainReloadHandler`, `TsvrcBuildCompile`'s callback order). |

### 2.2 The independence problem, and how to solve it

The user requirement driving this whole document: **tests must not depend on this
project's actual production scene or its actual `TsvrcConfig`/`TsvrcBuiltinConfig`
contents.** Two real obstacles stand in the way, and both have a concrete answer:

**Obstacle A — `FindRoot()` / `ScaffoldModule.FindCompiledType()` hardcode the
namespace+class name `Tsvrc.Core.Generated.TsvrcGenerated`, found via AppDomain
reflection.** There is exactly one such compiled type in the whole project (there can
only ever be one — that's the point of the generator). Module-level `Wire()` tests
therefore cannot use a fake/mock root type; they must use the **real, compiled**
`TsvrcGenerated` class. The independence comes from the *scene*, not the type: create
a throwaway Editor scene (`EditorSceneManager.NewScene(NewSceneMode.Single,
NewSceneSetup.EmptyScene)`, never saved, torn down in `[TearDown]` by restoring
whatever scene was open before — or simply not caring, since Edit Mode tests don't
persist scene state across the suite), instantiate the real compiled type on a fresh
GameObject in that scene via `UdonSharpUndo.AddComponent` or plain `AddComponent`,
and drive `Wire()` against *that* instance. This is real production code running
against synthetic, test-owned scene content — exactly what was asked for.

**Obstacle B — several modules' `LoadConfig()` reads a fixed builtin asset path**
(`TsvrcBuiltinConfig.asset`, `TsvrcTranslationConfig.asset` via `AssetDatabase.
LoadAssetAtPath`) which *is* shared, real project state, not test-controlled. The
fix: **don't call `LoadConfig()` in `Wire()`-focused tests.** Every module's `Wire()`/
`GenerateCode()` methods only ever read the module's private `_entries` (or
equivalent) field — never re-touch `LoadConfig()`'s data sources directly. So tests
can construct a module instance, populate its private entries field via reflection
with fully synthetic data (mirroring the existing `PoolModuleSlotMathTests` pattern
exactly), and call `Wire()`/`GenerateCode()` directly. This decouples every
config-merge-independent test from the real builtin config entirely. The handful of
tests that *do* need to exercise `LoadConfig()`'s merge/resolve logic (builtin +
scene, dedup, "scene wins" ordering) should instead call the **private
`Resolve`/`BuildEntries`/`ResolveConfig` methods directly** via reflection, passing
hand-built in-memory `TsvrcConfig`/`TsvrcBuiltinConfig` instances
(`ScriptableObject.CreateInstance`, never touching `AssetDatabase`) — never routing
through the real fixed asset path at all. Only `AfterFilesStable()`'s program-asset
creation (which legitimately needs `AssetDatabase.CreateAsset`) needs real, but
test-created-and-deleted, `.asset` files, written to a scratch subfolder and cleaned
up in `TearDown`.

**Net effect:** every test in this plan is fully self-contained — it creates whatever
scene objects, prefabs, or config assets it needs, and tears them down afterward.
None depend on the developer's actual world scene, actual `TsvrcConfig` entries, or
actual `TsvrcBuiltinConfig` contents.

### 2.3 Shared test infrastructure to build once (Phase 0)

- `Tests/Editor/CodeGen/TestUtil/TempSceneScope.cs` — `IDisposable` wrapping
  `EditorSceneManager.NewScene`, tracks and destroys every `GameObject` created
  during the test.
- `Tests/Editor/CodeGen/TestUtil/CompiledRootFixture.cs` — helper that finds the real
  compiled `TsvrcGenerated` type (skip the whole test class with `Assert.Ignore` if
  it's ever somehow absent — it always exists in this project, but a fresh clone
  before first domain reload is a real edge case worth guarding against) and adds it
  to a GameObject in the active (temp) scene.
- `Tests/Editor/CodeGen/TestUtil/PrivateFieldAccess.cs` — small reflection
  get/set-by-name helpers, since nearly every module test needs to poke a private
  `_entries`/`_poolEntries`/`_config` field. Centralize instead of repeating the
  reflection boilerplate per test file (currently duplicated across
  `PoolModuleSlotMathTests`, `FactorySanitizeTests`, `TranslationParsingTests`).
- Scratch-asset convention: `Assets/Tsvrc/Tests/Editor/CodeGen/__Scratch__/` for any
  test that must create real `.asset`/prefab files (program assets, factory/pool
  prefabs); delete the whole folder + `.meta` in `[TearDown]`, `AssetDatabase.Refresh()`
  once at suite `[OneTimeTearDown]`.

---

## Part 3 — The test list, simplest to most complex

Checkbox items. `[x]` = already implemented (cross-referenced against
`Tests/Editor/CodeGen/*`); `[ ]` = to write.

**Implementation status (2026-07-05):** Phases G0, G1, G2, and G3 are fully implemented
(249 tests, all passing in real Unity batch-mode `-runTests -testPlatform EditMode` runs).
Phases G4 and G5 are implemented for every module *except* the "field found → value
assigned" happy path on `SingletonModule`/`ConstructModule`/`FactoryModule`/`PoolModule`
(and `PoolModule`'s slot-field specifically) — see Part 4.5 for exactly why, and what
would unblock it. `PoolModule.Wire()`'s scene-mutation and external-`[WirePool]`-target
assignment (the part that doesn't depend on the missing bootstrap) is fully covered,
including the `CollectWireTargetsByType` finding in Part 4 item 5. Phase G6 covers
`CreateModules()` and (indirectly, via `TsvrcBuildCompileTests`) a real bootstrap-gated
`Run()` pass; the deeper orchestration items (G6.3 synthetic-module collision, G6.6
`_isWiring` timing, G6.7 `skipRefresh` fallthrough) remain unwritten — driving the real
`TsvrcGenerator.Run()` repeatedly across a test suite has a real, now-documented risk (see
`TsvrcBuildCompileTests`' own header comment) of leaving process-lifetime
`EditorApplication` hooks active for the rest of the batch, so each additional test that
exercises `Run()` needs the same care. Phase G7's `TsvrcAssetWatcher` short-circuit
branches are covered; `TsvrcDomainReloadHandler` remains an intentional low-value skip per
its own entry below. Phase 8 (manual) is unchanged - still manual.

### Phase G0 — Harness

- [ ] G0.1 `TempSceneScope` + `CompiledRootFixture` + `PrivateFieldAccess` helpers
      (§2.3). Prove the harness with one trivial test: create a temp scene, add the
      real compiled root, call `MemoryModule.Wire()` with a reflection-set empty
      config, assert no exception and no scene objects besides the root.
- [ ] G0.2 Scratch-asset folder convention + `[OneTimeTearDown]` sweep, proven via a
      test that creates and deletes a dummy `.asset`.

### Phase G1 — Pure logic, no Unity objects at all (CG-Pure)

- [x] G1.1 `TsvrcModule.AliasName` / `Deduplicate` — done
      (`TsvrcModuleHelpersTests.cs`).
- [x] G1.2 `UdonWriter` full API — done (`UdonWriterTests.cs`). **Gap check**: confirm
      `Class(modifiers, name, baseClass, attribute)` (distinct from `Block`) and
      `Region`/`EndRegion` interaction with nested `Block` indentation depth are both
      covered; add if missing.
- [x] G1.3 `FactoryModule.Sanitize` — done (`FactorySanitizeTests.cs`).
- [x] G1.4 `PoolModule` slot-count graph (`ComputeForType`/`ComputeTotalSlots`) —
      done (`PoolModuleSlotMathTests.cs`), covers isolated/leaf/parent-child/diamond/
      self-cycle. **Add**: multi-level chain (A→B→C, 3 deep) to confirm the
      topological order handles depth > 2, and a disconnected-forest case (two
      unrelated graphs computed in the same `_poolTypeInfos` dict don't cross-
      contaminate).
- [x] G1.5 `TranslationModule` parsing helpers — done (`TranslationParsingTests.cs`):
      `ParseLanguageJson`, `SanitizeIdentifier`, `EscapeString`. **Gap check**: confirm
      `BuildEnumNames`'s 3-way collision path (two languages sharing a sanitized
      `Label` AND the `Label_Key` fallback also collides, forcing the numbered
      suffix) is covered — this is a chained fallback, easy to under-test if only the
      first collision tier was exercised.
- [ ] G1.6 `TsvrcModule.IsTsvrcBehaviourType` (CG-Pure, no scene needed — pure type
      reflection over the AppDomain): a real `TsvrcBehaviour` subclass name → true; a
      plain `UdonSharpBehaviour` subclass (not extending `TsvrcBehaviour`) → false; a
      type name that exists in no loaded assembly → false; explicit empty-namespace
      vs. populated-namespace argument form (`ns=""` vs `ns=null` vs real namespace).
- [ ] G1.7 `TsvrcAssetWatcher.AnyMatch` (reflect the private static): empty paths
      array, empty watched set, case sensitivity (paths are matched via a
      case-*sensitive* `HashSet<string>` even though the watched-set itself was built
      case-*insensitively* upstream in `TsvrcGenerator` — confirm which comparer
      `AnyMatch` actually uses and pin the current behavior, flagging a mismatch if
      found), single match among many non-matching paths.
- [ ] G1.8 `PackagePaths.Root` — asserts it resolves to `"Assets/Tsvrc"` in this
      project (documented as inherently project-layout-coupled, not a fully isolated
      test — see §1.4). One assertion, low value beyond regression-pinning.
- [ ] G1.9 `TsvrcBuildCompile.OnBuildRequested` (CG-Smoke, no scene): `callbackOrder
      == -99`; `VRCSDKRequestedBuildType.Scene` → returns `true` and (spy via a test
      subclass of `TsvrcGenerator`? not possible, it's `internal static` — instead
      assert indirectly: after calling with `Scene`, the real generator's
      `WatchedPaths` is non-empty, proving `AfterDomainReload(skipRefresh:true)` ran);
      any non-`Scene` build type (e.g. `Avatar`) → returns `true` **without** touching
      `WatchedPaths` (capture its value before/after and assert unchanged).

### Phase G2 — `GenerateCode()` golden-file tests, synthetic entries (CG-Gold)

No scene needed — construct each module, set its private entries field via
reflection to hand-built values, call `GenerateCode()`, assert exact string output
(inline expected string; only externalize to a `Fixtures/CodeGen/*.txt` file if the
expected output exceeds ~30 lines, per the existing convention).

- [ ] G2.1 **ScaffoldModule.GenerateCode()** — the one truly parameter-free golden
      file; assert the exact fixed template string (namespace, class, `Start()` call
      order: Memory → Singleton → Pool → Construct → Instance).
- [ ] G2.2 **SingletonModule.GenerateCode()** — empty entries → stub (`_TsSingletonStart`
      with an empty body); one non-`TsvrcBehaviour` entry (field declared, no
      `TsConstruct` call generated); one `TsvrcBehaviour` entry (field + `TsConstruct`
      call generated); one `Animator` entry (name suffix applied); multiple entries →
      alphabetical-by-name ordering in both the field list and the `_TsSingletonStart`
      body, independent of config/insertion order; multiple `using` namespaces
      deduped and included.
- [ ] G2.3 **ConstructModule.GenerateCode()** — empty → stub; single/multiple entries
      → `_construct{Name}` field naming, alphabetical ordering, `TsConstruct` call per
      entry; using-namespace aggregation.
- [ ] G2.4 **FactoryModule.GenerateCode()** — empty → stub; the three branch bodies
      of `Create{Name}(Transform parent)`: bare `GameObject` (`return go;`),
      `TsvrcBehaviour` (`GetComponent` + conditional `TsConstruct` + return), other
      component type (`GetComponent` returned directly, no `TsConstruct`); confirm
      `_factory{Name}` field declared for every entry regardless of branch.
- [ ] G2.5 **PoolModule.GenerateCode()** — empty-after-filtering (`TotalSlots==0` for
      everything) → stub; per-type slot fields named `_pool_{Type}_{index}` for
      `0..TotalSlots-1`; `_TsPoolStart()` calls `TsConstruct` only for types where
      `IsTsvrcBehaviourType` is true, skipped entirely for plain-component pool types;
      output filtered/ordered by `TypeName` (entries with `TotalSlots == 0` excluded
      even if they were configured — confirm via a synthetic entry with 0 total
      slots).
- [ ] G2.6 **InstanceModule.GenerateCode()** — this one is **not** config-dependent
      (always emits the same fixed `_instance` field/`Instance` property/
      `_TsInstanceStart` shape regardless of `_detectedType`/`_ambiguous`) — confirm
      that's actually true by asserting identical output whether or not a detected
      type is set, and note this as a slightly surprising asymmetry vs. every other
      module (worth a one-line comment in the test itself, since it's easy to assume
      wrongly that this file's content varies).
- [ ] G2.7 **MemoryModule.GenerateCode()** — no config variation at all (like
      Scaffold/Instance); single golden-file assertion.
- [ ] G2.8 **TranslationModule.GenerateCode()** — empty languages → stub (enum with
      no members, `Translate` returning the key unchanged); single language → enum
      with one member, `SetLanguage`'s single `if`/`else` error-fallback shape,
      per-language `_tsKeys_{id}`/`_tsVals_{id}` array literals with correct
      escaping; multiple languages → chained `if`/`else if`/`else` in `SetLanguage`,
      one array pair per language; confirm the hardcoded `BatchSize = 20` literal
      appears in `_TsApplyTranslationBatch()`'s generated `Mathf.Min` call (regression
      pin — if someone changes the constant, this test should visibly need updating).
- [ ] G2.9 **Cross-module: alphabetical-order stability.** Explicit regression test
      per module that supports it (Singleton/Construct) — feed entries in
      deliberately *non*-alphabetical config order and assert generated order is
      alphabetical, not insertion order. Called out separately since it's the kind of
      thing that silently breaks if someone "helpfully" changes a `foreach` to
      iterate the raw list rather than `.OrderBy(...)`.

### Phase G3 — `LoadConfig()` / merge-resolution logic, synthetic in-memory configs (CG-Pure via reflection, no `AssetDatabase`)

Call the private `Resolve`/`BuildEntries`/`ResolveConfig` methods directly with
hand-built `ScriptableObject.CreateInstance<TsvrcConfig>()` /
`ScriptableObject.CreateInstance<TsvrcBuiltinConfig>()` instances — no real asset
files, no dependency on this project's actual builtin config (§2.2, Obstacle B).

- [ ] G3.1 **SingletonModule.Resolve()** — null entry → warn+skip
      (`LogAssert.Expect`); duplicate object reference → warn+skip (second occurrence
      dropped, first kept); name derivation: plain component → type name; aliased
      GameObject (`__Foo__`) → alias wins over type name; `Animator` type → alias-or-
      goName + `"Animator"` suffix specifically (not just type name + suffix);
      dedup applied across the *combined* scene+builtin stream (a builtin named "Foo"
      and a scene entry that would also be named "Foo" → second gets "Foo2",
      regardless of which list contributed which); scene entries fully precede
      builtin entries in iteration order (confirms "scene wins" priority claimed in
      the source comment — construct the case where both a scene and a builtin object
      would produce the same name and confirm the *scene* one keeps the un-suffixed
      name).
- [ ] G3.2 **ConstructModule.Resolve()** — same null/duplicate/alias/dedup shape as
      G3.1 but scene-only (no builtin source to merge — confirm there's genuinely no
      builtin `Constructs` concept anywhere, i.e. this is deliberately asymmetric with
      Singleton/Pool/Factory).
- [ ] G3.3 **FactoryModule.BuildEntries()** — non-persistent (scene) object passed as
      a "prefab" → warn+skip, verified via an in-memory `GameObject` that is never
      saved as an asset (`EditorUtility.IsPersistent` false); null entries in
      `Prefabs[]` silently skipped (no warning — confirm this is intentionally
      silent, unlike Singleton/Construct's null-slot warning, since it's a real
      asymmetry worth pinning); `Component` dragged in vs. `GameObject` dragged in
      both resolve to the same root prefab; group-name prefix applied via `Sanitize`
      then concatenated before per-prefab `Sanitize`, and `Deduplicate` applied across
      the *whole* merged builtin+user stream, keyed by the final prefixed name (two
      different groups producing the same final name after sanitization collide and
      dedup, not just within-group collisions); a group with `Prefabs == null` (vs.
      empty array) doesn't throw.
- [ ] G3.4 **PoolModule.ResolveConfig() + dedup-by-type-name** — builtin non-persistent
      entry → warn+skip; user non-persistent entry → warn+skip; same type registered
      in both builtin and user config → builtin occurrence kept (first-seen-wins,
      confirmed via `_poolEntries.Where(seenEntryTypes.Add(...))` — construct the
      case and assert the *builtin* prefab reference specifically survives, not just
      "some" entry survives).
- [ ] G3.5 **PoolModule.ScanExternalRefs() / ScanInternalDeps()** (needs a temp scene
      with synthetic `MonoBehaviour`s carrying `[WirePool]` fields — first CG-Wire-
      adjacent test in this phase, still config-independent): a `[WirePool]` field
      typed as an array or generic collection is explicitly excluded (`IsArray ||
      IsGenericType` guard) even if the element type matches a pool type — construct
      this and confirm it contributes zero to `ExternalCount`; a `[WirePool]` field
      that is neither `public` nor has `[SerializeField]` is excluded by
      `IsWirePoolField`'s serialization check; fields declared on a *base class* of a
      scene behaviour are still discovered (the `for (var t = ...; t.BaseType)` walk);
      a pool-type-on-pool-type internal dependency is *not* also double-counted as an
      external ref (a `[WirePool]` field on a class that's itself a pool type must go
      through `ScanInternalDeps`'s path, never `ScanExternalRefs`'s — confirm the
      `if (_poolTypeInfos.ContainsKey(...)) continue;` guard in `ScanExternalRefs`
      actually excludes it).
- [ ] G3.6 **TranslationModule.ParseLanguageFiles()** (multi-file orchestration, one
      level above the already-tested single-file `ParseLanguageJson`) — one good file
      + one malformed file → the malformed one is skipped, the good one still
      produces a `LanguageEntry` (partial-failure isolation, not "one bad file kills
      the whole run"); null entries in `LanguageFiles[]` skipped.
- [ ] G3.7 **TranslationModule.FindTmpTargets()** (temp scene with real
      `TextMeshProUGUI` objects — first genuine CG-Wire-style scene test) — name
      matching *and* key-presence are both required (a correctly-`_key_`-shaped name
      with no matching JSON key is excluded, silently, per §1.3); name shape
      rejected by `TmpTargetPattern` even if the bare key exists in the JSON (e.g.
      `"key_"` missing the leading underscore, or `"_key"` missing the trailing one)
      is excluded; case sensitivity of the name-to-key match (confirm it's ordinal,
      since `HashSet<string>` default comparer differs from `Contains` overload used).

### Phase G4 — `Wire()` / scene mutation, isolated temp scene + real compiled root (CG-Wire)

Uses the `TempSceneScope` + `CompiledRootFixture` harness from G0. Populate the
module's private entries field via reflection (bypassing `LoadConfig()` entirely, per
§2.2) with synthetic `Component`/prefab fixtures created for the test. Simplest
modules first.

- [ ] G4.1 **MemoryModule.Wire()** — root found, `TsMemory` present in scene → field
      assigned; `TsMemory` absent → field left/set null, no throw; field-not-found
      (stale scaffold — remove the field from the fake so `so.FindProperty` returns
      null) → warns, doesn't throw; re-`Wire()` when the value hasn't changed is a
      no-op (`prop.objectReferenceValue == memory` short-circuit — assert
      `so.ApplyModifiedProperties()` isn't even attempted, e.g. by asserting the
      scene isn't marked dirty on the second call).
- [ ] G4.2 **MemoryModule.AfterFilesStable()** — creates `TsvrcMemory` child under
      root when absent; leaves it alone when present; program-asset-missing return
      value semantics (true only when the `.asset` truly doesn't exist yet).
- [ ] G4.3 **SingletonModule.Wire()** — direct reference assignment for N synthetic
      entries; field-not-found on one entry warns but doesn't abort wiring the
      remaining entries (confirm it's a `continue`, not a `return`, on missing-field).
- [ ] G4.4 **ConstructModule.Wire()** — same shape as G4.3; additionally: an entry
      whose `SourceObject` is null (can happen if the source `TsvrcBehaviour` was
      destroyed between `LoadConfig()` and `Wire()`) warns+skips without touching
      other entries.
- [ ] G4.5 **ScaffoldModule.EnsureRootSceneObject()** (via `AfterFilesStable()`, since
      it's not itself public) — zero existing instances + no same-named GameObject →
      new GameObject created at sibling index 0; zero instances + a same-named
      GameObject without the component → component added to it, no new GameObject
      created; multiple existing instances → all but the first destroyed, both
      scenes marked dirty if they differ (unlikely in a temp single-scene setup, but
      confirm the loop touches every duplicate's own scene, not just the first's);
      multiple same-named-but-componentless GameObjects → same "keep first, destroy
      rest" behavior for that path too.
- [ ] G4.6 **ScaffoldModule.EnsureChildSceneObject()** — child absent → created +
      component added (`isUdonSharp: false` path for `TsvrcConfig`, `isUdonSharp:
      true`/default path exercised by `MemoryModule`'s `EnsureChildSceneObject`
      call — confirm both branches directly); child present but missing the
      component → component added to the *existing* GameObject (not recreated);
      child present with correct component already → no-op (no `Undo` calls — assert
      indirectly via "not marked dirty" if there's no easier signal); `editorOnly:
      true` self-heals a wrong/missing tag on an existing child even when the
      component was already correct (the tag-check is independent of the
      component-check).
- [ ] G4.7 **ScaffoldModule.NormalizeProgramAsset()** — asset with already-sorted
      `fieldDefinitions` → returns `false`, doesn't touch/dirty the asset; unsorted →
      returns `true`, dictionary re-sorted, asset saved; empty `fieldDefinitions` →
      `false`; reflection-miss path (asset's type genuinely lacks a
      `fieldDefinitions` field — simulate via a plain `ScriptableObject` stand-in, not
      a real `UdonSharpProgramAsset`, to hit the "field doesn't exist" branch without
      needing a broken SDK version) → returns `false` silently, no exception.
- [ ] G4.8 **InstanceModule.Wire()** — zero detected type: existing child removed +
      field cleared; one detected type, no existing child: child created, component
      added, program asset created, field set; one detected type, existing child with
      correct component and current program asset: no-op recreate (confirm via "no
      new program asset write"); existing child with wrong/stale component type:
      old component(s) destroyed via `UdonSharpUndo.DestroyImmediate`, new one
      created; existing child with correct component **but deleted program asset**:
      still recreates (this is the subtle "recreate on programAssetMissing
      independent of component-type check" branch — construct a fixture where the
      component type matches but the `.asset` file was deleted, and confirm it still
      goes through the destroy+recreate path, not a silent no-op); `_ambiguous ==
      true`: `Wire()` is a complete no-op regardless of any other state (construct a
      case with an existing, perfectly valid child, force `_ambiguous = true` via
      reflection, and confirm nothing is touched — this is the one deliberate
      "leave broken things broken rather than guess" branch in the whole generator,
      worth its own explicit non-negotiable test).
- [ ] G4.9 **FactoryModule.Wire() + IsFactoriesAlreadyWired()** — empty entries with
      an existing "Factories" child → child destroyed; empty entries, no existing
      child → no-op; N entries, no existing child → "Factories" container created,
      each prefab instantiated inactive, named to match, field assigned; already-
      wired (matching child count/prefab source/inactive state/field reference for
      every entry) → **no destroy+recreate**, assert no new GameObjects created on a
      second `Wire()` call with identical entries (idempotency is the single most
      important property of this method — dedicate more than one test to it): (a)
      unchanged config → no-op, (b) child count changed (one entry added) → full
      rebuild, (c) same count but one entry's prefab source swapped → full rebuild,
      (d) an instantiated factory object manually set `SetActive(true)` by the user
      → full rebuild (proves the "inactive by default" invariant self-heals), (e)
      the field's object reference manually cleared by the user → full rebuild.
      Also: instantiation failure (prefab asset made to return null from
      `PrefabUtility.InstantiatePrefab` — simulate via a destroyed/invalid prefab
      reference) warns and continues to the next entry rather than aborting.
- [ ] G4.10 **PoolModule.Wire() + IsPoolAlreadyWired()** — mirror the depth of G4.9
      for Pool's extra two-phase complexity: (a) empty entries + existing "Pool"
      container → destroyed; (b) N slots across 2+ types, no existing container →
      container created, correct instance count per type, `_pool_{Type}_{i}` fields
      assigned in order, external `[WirePool]` targets on synthetic scene behaviours
      correctly wired to the new instances; (c) already-wired idempotency, same
      five-way breakdown as G4.9(a-e) adapted to Pool's child-count/prefab-source/
      per-slot-field/per-target-field checks; (d) **the candidate bug from §4**:
      configure a valid pool entry, `Wire()` once (creates a real container), then
      make all entries invalid (e.g. clear `_poolEntries` to simulate every entry
      being skipped by `ResolveConfig` while `_hasAnyConfigured` stays true) and
      `Wire()` again — assert on **current** behavior (documents it either way) of
      whether the stale "Pool" container from the first run is left behind; (e)
      instance's created `GameObject` missing the expected component after
      `PrefabUtility.InstantiatePrefab` (corrupted prefab) → that slot destroyed
      immediately, `instances.Add(null)`, later phases skip the null slot without
      throwing; (f) `[WirePool]` target count vs. slot count mismatch in both
      directions logs the correct one of the two distinct warning messages
      (`"...slot(s) unassigned"` vs. `"...will keep stale references"`).
- [ ] G4.11 **TranslationModule.Wire()** — `_languages.Count == 0` → no-op (field
      untouched, even if targets exist — confirm this "wiring is skipped entirely
      when there's no language data" branch, since it means stale `_translationTargets`
      from a prior run linger until languages are reconfigured, which is worth
      knowing); non-empty languages → `_translationTargets` array resized and
      populated to match `_cachedTmpTargets` exactly, including shrinking (fewer
      targets than a prior run) and growing.
- [ ] G4.12 **TranslationModule.AfterFilesStable() / OnSceneHierarchyChanged() via
      SyncEffectiveKeys()** — identical target-name set across a hierarchy churn
      (same names, different GameObject instances after a reorder/reparent) → returns
      `false`, `Wire()` correctly not re-triggered; a genuinely new/removed target
      name → returns `true`.

### Phase G5 — `OnSceneHierarchyChanged()` self-healing signal, isolated temp scene (CG-Wire)

These decide whether a rerun gets scheduled at all — under-testing them means broken
wiring silently never self-heals.

- [ ] G5.1 **ScaffoldModule.OnSceneHierarchyChanged()** — compiled type not found yet
      → `false`; wrong instance count (`0` or `2+`) → `true`; exactly one instance but
      missing the `TsvrcConfig` child → `true`; exactly one instance with the child
      present → `false`.
- [ ] G5.2 **MemoryModule.OnSceneHierarchyChanged()** — root absent → `false`; root
      present, `TsvrcMemory` child absent → `true`; present → `false`.
- [ ] G5.3 **InstanceModule.OnSceneHierarchyChanged()** — `_ambiguous` true → always
      `false` (never self-triggers while ambiguous, matching Wire()'s own refusal to
      act); `_detectedType == null` → `false`; single detected type, root absent →
      `false`; root present, child absent → `true`; child present → `false`.
- [ ] G5.4 **FactoryModule.OnSceneHierarchyChanged()** — `_entries.Count == 0` →
      `false` unconditionally (even if a stray "Factories" GameObject exists from a
      prior config — confirm this asymmetry vs. Pool's equivalent check, which *does*
      look for a stray container when entries are empty); root absent → `false`;
      "Factories" child present vs. absent with non-empty entries.
- [ ] G5.5 **PoolModule.OnSceneHierarchyChanged()** — `_poolEntries.Count == 0` →
      reflects presence/absence of a stray "Pool" child directly (the asymmetry noted
      in G5.4); non-empty entries, container absent → `true`; container present with
      wrong total child count → `true`; correct count → `false` (note: this only
      checks *count*, not per-slot identity — a same-count-but-wrong-type-or-order
      corruption would NOT be caught by this check and would rely on
      `IsPoolAlreadyWired`'s deeper check inside the next `Wire()` call instead;
      confirm this division of responsibility explicitly with a test that corrupts
      slot 0's prefab identity while preserving total count, expecting
      `OnSceneHierarchyChanged() == false` but the next `Wire()` to still detect and
      repair it via `IsPoolAlreadyWired`).
- [ ] G5.6 **TranslationModule.OnSceneHierarchyChanged()** — covered by G4.12
      (same `SyncEffectiveKeys` implementation, called from both hooks).

### Phase G6 — Orchestration (`TsvrcGenerator.Run()` itself) (CG-Orch)

Hardest tier — some of this may require accepting a small amount of real-project
coupling (documented per-item) since `TsvrcGenerator` is a static class with static
state, not trivially instance-per-test-isolated. Prefer driving through public/
internal entry points (`Run`, `ManualGenerate`, `ScheduleRerun`) and asserting via
`WatchedPaths`/log output/scene state rather than reflecting into private statics
where avoidable, but don't rule out reflection where it's the only way to observe
`_activeModules`/`_isWiring`/`_rerunPending` directly.

- [ ] G6.1 **Bootstrap gate** — in a temp scene with **no** `TsvrcConfig`, **no**
      compiled-type instance, and (this is the hard part — see note) no
      `TsvrcInstance` subclass reachable: `Run(allowBootstrap:false)` should do
      nothing observable (no files written — assert via mtime/hash of the generated
      folder before/after, or simpler, temporarily point `GeneratedFolder`-equivalent
      assertions at whether `WatchedPaths` changes). **Note**: because this project's
      own compiled `TsvrcGenerated` type and possibly a real `TsvrcInstance` subclass
      already exist in the AppDomain for *other* reasons (this very codebase uses
      Tsvrc), a from-scratch "project has never bootstrapped" state can't be
      faithfully reproduced inside this same project's test run — document this as a
      known, permanent limitation rather than a task to solve, and instead test the
      *logic* of `HasBootstrapSignal()` (§G1-equivalent, already listed) as the real
      unit of coverage; treat full end-to-end "Run() literally does nothing" as
      untestable-in-this-project and out of scope.
- [ ] G6.2 **`ManualGenerate()` sets `allowBootstrap: true`** — confirm (by calling it
      in a scene that would otherwise fail the bootstrap gate, if such a scene can be
      constructed per the note above — otherwise this collapses into "same as a
      normal Run() once bootstrapped," lower value) that it proceeds regardless.
- [ ] G6.3 **Cross-module field-name collision** — since only `SingletonModule`
      currently overrides `ExposedFieldNames()`/`ExcludeFieldNames()` (§1.1, §4), the
      *only* way to exercise `TsvrcGenerator.Run()`'s duplicate-detection loop with a
      genuine cross-module collision is to register two **synthetic test-only**
      `TsvrcModule` subclasses (not the real 8) that both expose the same field name,
      run the collision-detection block in isolation (extract/call it the same way —
      may require factoring the small dup-detection loop out of `Run()` into an
      internal testable helper if it isn't already separately callable; flag this as
      a possible small refactor motivated directly by a testability gap, not
      speculative cleanup) and assert both modules receive the conflicting name via
      `ExcludeFieldNames`.
- [ ] G6.4 **`WatchedPaths` aggregation** — each module's own `FileName` is always
      added; `WatchedAssets()` from every module is unioned; case-insensitive
      comparer confirmed (add the same path in two different cases from two fake
      modules, assert only one entry survives).
- [ ] G6.5 **`WriteIfChanged` idempotency** — identical `GenerateCode()` output on
      successive calls does not rewrite the file (assert via file mtime unchanged,
      or a directly-callable extraction if one gets factored out per the Phase 1
      note in `TESTING_PLAN.md`); changed output does rewrite.
- [ ] G6.6 **`_isWiring`/`_justFinishedWiring` suppression** — the trickiest
      concurrency-shaped (but single-threaded/reentrant, not multi-threaded) bit of
      the whole module: trigger `Wire()` and confirm that a `SerializedObject` write
      performed *by* `Wire()` itself does not synchronously cause
      `OnPostprocessModifications` to schedule a rerun (assert `_rerunPending`, via
      reflection, is false immediately after `Wire()` returns but before the deferred
      `EditorApplication.delayCall` clearing `_justFinishedWiring` has had a chance to
      run — this exact ordering is the entire point of the two-flag design and is the
      single highest-value orchestration test in this phase).
- [ ] G6.7 **§1.2.a skip-Refresh-but-fall-through-to-Wire()** — construct the
      `skipRefresh: true` path (as `TsvrcBuildCompile` does) with a change pending in
      `GenerateCode()` output, and confirm `Run()` does *not* throw and does not
      corrupt scene state even though it's wiring against a stale compiled type;
      this is explicitly a "pin current behavior + flag if surprising" test, not a
      correctness assertion, since whether this is *desired* behavior needs a human
      call (see §4).
- [ ] G6.8 **Rerun suppression during Play Mode** — `Run()`/`OnHierarchyChanged()`/
      `WaitForBootstrapSignal()` all early-return when
      `EditorApplication.isPlayingOrWillChangePlaymode` — this can be asserted in
      Edit Mode by temporarily... actually can't be forced true outside real Play
      Mode entry; lowest-value item in this phase, consider a `[UnityTest]` that
      enters Play Mode briefly *solely* to confirm zero generator file writes occur,
      accepting the Play-Mode-Test-Runner-reporting caveat from `TESTING_PLAN.md`
      applies here too (verify via Console log / file mtime, not green-checkmark
      trust).

### Phase G7 — Cross-cutting glue (CG-Smoke)

- [ ] G7.1 **`TsvrcAssetWatcher.OnPostprocessAllAssets`** — reflect the private
      static method, call directly with synthetic path arrays and
      `didDomainReload: true` → no rerun scheduled regardless of path overlap
      (short-circuits immediately); `didDomainReload: false` with `WatchedPaths`
      empty → no rerun; `didDomainReload: false` with a matching path in each of the
      four arrays independently (imported/deleted/moved/movedFrom) → rerun scheduled
      for each.
- [ ] G7.2 **`TsvrcDomainReloadHandler`** — one assertion: static constructor
      enqueues exactly one `delayCall` (can't easily force a real domain reload in a
      single Edit Mode test run; treat as effectively covered by G6's `Run()`-level
      tests plus a code-reading confirmation that the constructor body is a single
      `delayCall +=` line with no loop/conditional that could double-subscribe).

---

## Part 4 — Candidate bugs / design gaps found during this analysis

These surfaced from reading the generator source closely while planning the tests
above, **not** from running anything yet. Per the stated priority (cover what's
implemented first; let real test-writing surface bugs), don't fix these yet — but
each has a specific test already called out above that will confirm or refute it.
Revisit this list once the corresponding phase is implemented.

1. **PoolModule stale-container leak** (G4.10(d)) — `Wire()`'s guard
   `if (_hasAnyConfigured && _poolEntries.Count == 0) return;` executes *before* the
   very next block that would destroy a stale "Pool" container
   (`if (_poolEntries.Count == 0) { destroy existing; return; }`), meaning that
   second block is dead code whenever `_hasAnyConfigured` is true. Concretely: if a
   project has a valid pool config, runs `Wire()` once (creating real pool
   instances), and then every pool entry becomes invalid (e.g. all configured
   prefabs deleted, while `_hasAnyConfigured` remains true because the *config
   array itself* still has non-null-but-now-invalid-persistence entries), the old
   "Pool" GameObject and its instantiated children are never cleaned up. Worth
   confirming with a test and then a decision on whether this is the intended
   "leave broken things alone" philosophy (matching `InstanceModule`'s deliberate
   ambiguous-no-op) or an oversight.
2. **Cross-module `ExposedFieldNames` is effectively single-module today**
   (§1.1, G6.3) — the collision-detection mechanism in `TsvrcGenerator.Run()` is
   general (any module can override `ExposedFieldNames`/`ExcludeFieldNames`), but
   only `SingletonModule` does. `ConstructModule`/`FactoryModule`/`PoolModule` all
   use prefixed field names (`_construct`/`_factory`/`_pool_`) which happen to make
   cross-module collisions structurally impossible today — but that means the
   general mechanism is currently only ever tripped by two Singleton entries
   deriving the same name, which `SingletonModule.Resolve()`'s own `Deduplicate`
   call already prevents before `ExposedFieldNames()` is even consulted. In its
   current form the cross-module path may be unreachable through any real module
   combination; only the synthetic-module test in G6.3 can currently exercise it.
   Not necessarily a bug — just worth the maintainers knowing the safety net has
   never actually fired in practice.
3. **`Run()`'s `skipRefresh` control flow** (§1.2.a, G6.7) — when called with
   `skipRefresh: true` (build-time) and pending `.cs` changes exist, execution falls
   through to `Wire()` against the *previous* compiled type rather than stopping.
   Whether this is safe depends on UdonSharp's own build-time compile happening
   later in the same build pass and re-wiring correctly — outside this module's
   control to verify directly. Flag for a human decision once G6.7's pinning test
   exists and its actual current behavior is visible in a real run.
4. **`TsvrcAssetWatcher.AnyMatch` comparer** (G1.7) — confirm whether the
   case-insensitive construction of `WatchedPaths` in `TsvrcGenerator` (`new
   HashSet<string>(StringComparer.OrdinalIgnoreCase)`) is actually honored by
   `AnyMatch`'s lookup (`watched.Contains(path)` — correct, since `Contains` uses
   the set's own comparer) or whether a fresh `HashSet` gets rebuilt somewhere with
   a different comparer. Likely fine, but cheap to pin with a test given the
   case-sensitivity of filesystem paths varying by OS.
5. **`PoolModule.CollectWireTargetsByType` doesn't apply the same serialization filter as
   `ScanExternalRefs`/`ScanInternalDeps`** (confirmed while implementing `PoolModuleWireTests`,
   2026-07-05) — `IsWirePoolField` (used by the two `Scan*` methods that compute slot counts)
   requires a `[WirePool]` field to be `public` or carry `[SerializeField]`; a private field
   with neither is correctly excluded from slot-count math. `CollectWireTargetsByType` (used by
   `Wire()`'s actual field-assignment pass) has its own separate, looser inline filter that only
   excludes array/generic fields — it does **not** check public/`[SerializeField]` at all. The
   practical effect: a non-serialized private `[WirePool]` field contributes zero to the slot
   count (so no extra pool instance gets created for it) but *is* counted in the target-count
   mismatch warnings and gets its own separate `"has [WirePool] but is not serialized"` warning
   at assignment time, inflating the apparent target/slot mismatch by one for every such field in
   the scene. Confirmed via `PoolModuleWireTests.Wire_NonSerializedWirePoolField_...` and the
   corrected expected count in `Wire_MoreExternalTargetsThanSlots_...`. Not fixed — flagged for a
   maintainer decision on whether `CollectWireTargetsByType` should reuse `IsWirePoolField`
   directly (it would need to become non-private, or the check inlined identically) so the two
   scans agree on what counts as a wireable field.

---

## Part 4.5 — Constraint discovered while implementing Phase G4 (2026-07-05)

While implementing Wire() tests against the real compiled root (§2.2, Obstacle A), a second,
more consequential environment constraint surfaced: **this project has never been bootstrapped
with a real, non-empty `TsvrcConfig`.** There is no committed `TsvrcInstance` subclass and no
configured Singletons/Constructs/Factories/PooledObjects anywhere in the repo yet - the
committed `Assets/TsvrcGenerated/*.cs` files are all in their empty "stub" form, and (until
this session) `Assets/TsvrcGenerated/TsvrcGenerated.asset` (the program asset) didn't exist at
all, only `.cs` sources. `CompiledRootFixture` now self-heals that specific missing-asset case
the same way `ScaffoldModule.AfterFilesStable()` would (see its comment) - but it cannot
conjure fields that were never generated.

This matters because `SingletonModule`/`ConstructModule`/`FactoryModule`/`PoolModule` each
generate their serialized fields under **config-derived names** (`GameManager`,
`_constructHudManager`, `_factoryBullet`, `_pool_StateManager_0`, ...) - those fields only
exist on the compiled `TsvrcGenerated` type once a real `GenerateCode()` pass ran against real
config *and* a domain reload compiled the result. A single Edit Mode `[Test]` cannot trigger
and wait out a real domain reload mid-test, so **the "field found → value actually assigned"
happy path for these four modules' `Wire()` cannot be exercised against this project's real
compiled root today** - only the "field not found → warns, doesn't throw, other entries still
processed" branch is reachable.

Two fields were still fully testable end-to-end because they are unconditional (always
generated regardless of config): `MemoryModule`'s `_memory` and `InstanceModule`'s `_instance`.
`SingletonModuleWireTests` exploits this directly - it targets `_memory` as a stand-in "real
field" to validate the exact same mechanical assignment path a true Singleton field would use,
since `SingletonModule.Wire()` has no idea which module owns a field name, only that
`SerializedObject.FindProperty(name)` resolves and the source object's type matches.

**What would unblock full Phase G4 coverage for Factory/Pool/Translation's happy paths:**
either (a) a one-time real bootstrap - add a `TsvrcConfig` with real entries to the actual
project scene, run `Tsvrc > Force Regenerate`, let it settle through however many domain
reloads it takes, and commit the resulting non-stub generated files, so future test runs have
real fields to target; or (b) accept the current scope (missing-field/idempotency/scan-logic
paths only) as the practical ceiling for solo Edit-Mode-test coverage of these four modules and
rely on Phase 8's manual Build & Test gate to catch anything the happy path would have caught.
Recorded here rather than silently worked around, since it changes what "100% covered" can
mean for this specific slice of the module.

## Part 5 — Cross-reference to `TESTING_PLAN.md`

`TESTING_PLAN.md`'s "Coverage checklist" **Editor/CodeGen** row now points here in
full; its own Phase 1 items 1.1–1.7 and Phase 6 (6.1–6.13) have been superseded by
this document's Phase G0–G7 and removed from that file to avoid two sources of truth.
`TESTING_PLAN.md` Phase 7.1–7.3 (`TsvrcWindow`, `ObjectListGUI`, the two property
drawers) remain there — they're generic Editor-tool GUI infrastructure, not
generation/validation logic, even though `TsvrcModule.DrawTab()` is technically part
of the module contract described in §1.1 here.

Once every checkbox above is checked, cross-check against Part 1 one more time: if a
sentence in the architecture description doesn't map to at least one checklist item,
that's a gap in this plan, not just in the test suite.
