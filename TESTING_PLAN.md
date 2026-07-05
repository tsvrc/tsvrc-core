# Tsvrc Test Suite — Implementation Plan

Companion to `TESTING.md` (the research/strategy doc). This file is the concrete,
ordered todo list: every class in the library, every kind of test it needs, from
simplest to most complex. Check items off as they're implemented.

**CodeGen scope moved out (2026-07-04):** everything under `Editor/CodeGen/` (the
generator: `TsvrcGenerator`, `TsvrcModule` and all 8 modules under `Modules/`,
`UdonWriter`, `PackagePaths`, `TsvrcAssetWatcher`, `TsvrcBuildCompile`,
`TsvrcDomainReloadHandler`) now has its own from-first-principles full-coverage plan
in **`CODEGEN_TESTING_PLAN.md`**, which supersedes this file's old Phase 1 items
1.1–1.7 and all of Phase 6. Editor/Configure and Editor/Tools (`TsvrcWindow`,
`ObjectListGUI`, property drawers, `MeshCombiner*`, `TsvrcTranslationWindow`) stay
here — they're generic Editor-tool GUI, not generation/validation logic.

`Tests/Editor/TsArrayTests.cs` is a **proof of concept only** — it validates the
harness works (asmdefs, NSubstitute wiring, UdonSharpBehaviour-in-EditMode). It is
not "the TsArray test" in a final sense; Phase 1 below re-scopes and extends it as
part of the real suite, following its established style (NUnit `[Test]`/`[TearDown]`,
`MethodUnderTest_Scenario_ExpectedResult` naming, real GameObjects tracked in a list
and cleaned up in `TearDown`).

## How this plan is organized

Six test kinds appear repeatedly. Shorthand used throughout:

| Tag | Meaning | Runner |
|---|---|---|
| **EM-Pure** | Edit Mode, zero Unity/VRC objects — plain static logic | `Tests/Editor` |
| **EM-Obj** | Edit Mode, needs real `GameObject`/`Texture2D`/etc. but no Udon lifecycle/scene | `Tests/Editor` |
| **PM-Solo** | Play Mode, single instance/single (local) player, no ownership races | `Tests/PlayMode` |
| **PM-Scene** | Play Mode, needs a built prefab/scene hierarchy (multiple wired components) but still solo-player | `Tests/PlayMode` |
| **PM-Multi** | Play Mode, requires 2+ simulated players / ClientSim — ownership, join/leave, replication races | `Tests/PlayMode` (ClientSim) |
| **Gold** | Editor test asserting generated code/text output against a fixture (golden file or inline expected string) | `Tests/Editor` |
| **Smoke** | Editor test that just opens a window / runs a callback and asserts "no exception", not behavior | `Tests/Editor` |
| **Manual** | Cannot be automated — human-driven Build & Test / VCC / GUI interaction | N/A |

---

## Known environment constraint: Test Runner pass/fail is unreliable for Play Mode

Discovered while building the Phase 0.2 ClientSim harness proof-of-concept
(`Tests/PlayMode/ClientSim/ClientSimHarnessTests.cs`): **Unity's Test Runner window,
and `-runTests -testPlatform PlayMode` in batch mode, cannot reliably report a
pass/fail result for any Play Mode test in this project.**

**Root cause is not fully confirmed.** One real, contributing factor was identified
and ruled a dead end: `VRC.Core.UnityEventFilter`
(`Packages/com.vrchat.worlds/Runtime/VRCSDK/SDK3/UnityEventFilter.cs`), VRChat SDK3's
anti-cheat UnityEvent sanitizer, hooks `EditorApplication.playModeStateChanged`
unconditionally and on every Play Mode entry strips UnityEvent bindings it doesn't
recognize — including, observably in the Console log, bindings on Unity Test
Framework's own "Code-based tests runner" object
(`PlayModeRunnerCallback.TestStarted/TestFinished/RunStarted/RunFinished`). A
reflection-based unsubscribe of that specific filter handler was tried as a fix and
**did not resolve the hang** — the Play Mode session still never terminates on its
own, in both the interactive Test Runner window and headless
`-runTests -testPlatform PlayMode` batch mode, with or without that unsubscribe. The
stuck process was confirmed to be actively spinning (real, growing multi-thread CPU
usage), not blocked on a dialog or I/O — so something is still preventing "this run is
finished" from propagating, and the exact mechanism is unknown. That patch attempt was
removed from the project (unconfirmed fix, not worth the added complexity/risk of a
reflection-based event unsubscribe) rather than kept around as dead speculative code.

This is not caused by any Tsvrc code — the test's own logic completes correctly every
single time per Console output (ClientSim starts, player spawns, gets id, becomes
master, teleports, no errors). It will affect **every** Play Mode test in Phases 3–5,
not just this one, until someone roots the actual cause (next step would be attaching
a debugger to the hung process, or bisecting with deliberate `Debug.Log` breadcrumbs
through ClientSim's own coroutines to find exactly which yield never resumes).

**Practical mitigation for every Play Mode test going forward:**
- Verify correctness by reading the Console log for explicit `Assert`/`Debug.Log`
  output from the test itself — not by trusting the Test Runner window's green/red
  indicator, which will never populate for a Play Mode run.
- Expect to manually stop Play Mode (toolbar Play button) after visually confirming
  success/failure in the Console; it will not exit on its own.
- Do not attempt batch-mode (`-runTests -testPlatform PlayMode`) CI automation for
  this project without first solving this — it will hang indefinitely and needs to be
  killed externally (e.g. `taskkill`).
- Edit Mode tests (`Tests/Editor`, Phases 1, 2, 6, 7) are unaffected — this only
  applies to Play Mode. **Confirmed working**: `-batchmode -runTests -testPlatform
  EditMode -testResults <path>` runs cleanly end-to-end (verified running all of
  Phase 1: 95/95 Tsvrc tests passed, clean exit, real results XML in ~25s). Edit Mode
  is fully viable for CI/headless automation in this project; only Play Mode is not.

---

## Phase 0 — Test architecture & harness setup

- [x] 0.1 Confirm the U# Assembly Definition step from `TESTING.md` is done (Runtime
      compiles to Udon); this blocks every Play Mode test.
- [x] 0.2 Install ClientSim via VCC (bundled with `com.vrchat.worlds`, no separate
      package needed in this SDK version); confirmed the harness itself works via
      `Tests/PlayMode/ClientSim/ClientSimHarnessTests.cs` — verified by Console log
      output (ClientSim starts, spawns local player, assigns id, becomes master,
      teleports to spawn, zero errors), repeatably, across multiple runs. Its
      Test-Runner-reported pass/fail is **not** usable — see "Known environment
      constraint" above; verify future Play Mode tests via Console log output.
- [x] 0.3 Disable Domain Reload (Edit > Project Settings > Editor > Enter Play Mode
      Settings) — required for reliable Play Mode runs per known ClientSim/UdonSharp
      gotcha.
- [x] 0.4 Decide and document folder layout inside `Tests/Editor` and `Tests/PlayMode`,
      mirroring `Runtime/`/`Editor/` subfolders 1:1 so any class's test is easy to find.
      Populated on-demand per phase (Unity doesn't need empty placeholder folders);
      current actual state: `Tests/Editor/TsArrayTests.cs` (proof-of-concept, to be
      moved into `Utils/` and extended in Phase 1), `Tests/PlayMode/ClientSim/
      ClientSimHarnessTests.cs` (Phase 0.2 harness proof-of-concept, kept as the
      reference pattern for Phase 5's ClientSim-based tests — see environment-
      constraint note above for its log-only verification status). Target layout:
      ```
      Tests/Editor/
        CodeGen/
          UdonWriterTests.cs
          TsvrcModuleHelpersTests.cs      (AliasName, Deduplicate)
          Modules/
            PoolModuleSlotMathTests.cs
            FactorySanitizeTests.cs
            TranslationParsingTests.cs
            ConstructModuleGoldenTests.cs
            SingletonModuleGoldenTests.cs
            ScaffoldModuleGoldenTests.cs
            ... (one file per module, split pure-logic vs golden vs wiring)
        Editor/
          MeshCombinerToolTests.cs
          MeshCombinerWindowLogicTests.cs
          TranslationWindowRegexTests.cs
        Utils/
          TsArrayTests.cs (existing, extend)
          TsJsonTests.cs
          TextureGraphics2DTests.cs
        Fixtures/                         (golden-file .txt / sample JSON assets)
      Tests/PlayMode/
        Core/
          TsvrcBehaviourTests.cs
          TsvrcProcessTests.cs
          TsvrcInstanceTests.cs
        Utils/
          TsMemoryTests.cs
        Timing/
          TsvrcTimerTests.cs
        StateMachine/
          StateManagerTests.cs
        Tracking/
          PlayerTrackerTests.cs
          AutoPlayerTrackerTests.cs
          ReadyCheckProcessTests.cs
        DataTransfer/
          DataChunkerTests.cs
          ChunkedTransferSessionTests.cs
          DataSenderTests.cs
          DataChunkReceiverTests.cs
          DataSenderReceiverTests.cs
          DataTransfererIntegrationTests.cs
        Player/
          HeadClipGuardTests.cs
          TsPlayerTests.cs
        Session/
          RankedGameSessionTests.cs
        UI/
          TsListTests.cs
          TsListItemTests.cs
          TsvrcPlayerPositionOverlayTests.cs
      ```
- [ ] 0.5 Add a shared `Tests/PlayMode/TestUtil/` helper: spawn/destroy tracked
      `GameObject`s (same pattern as `TsArrayTests`), a builder for a minimal
      `TsvrcRoot` test-double subclass (returns fake `TsvrcInstance`/`TsMemory`),
      and — once ClientSim is in — a helper to spin up N simulated players and tear
      them down in `[TearDown]`/`[UnityTearDown]`.
- [ ] 0.6 Adopt the Guribo/UdonUtils pattern from `TESTING.md` Layer 5: introduce a
      `TSVRC_UNIT_TESTING` compilation symbol (or reuse `UNITY_INCLUDE_TESTS`) and,
      where a class calls `Networking.*`/`VRCPlayerApi` directly in a way that blocks
      solo Edit/Play Mode testing, wrap it behind a small interface (e.g.
      `INetworkingProvider`) so NSubstitute can stand in for it. Apply this
      opportunistically as Phases 3-5 hit classes that are otherwise untestable
      solo — don't do a big upfront refactor.
- [ ] 0.7 Golden-file convention for Phase 6 (CodeGen): store expected generated
      source as literal C# strings in the test (small) or as checked-in `.txt`
      fixtures under `Tests/Editor/Fixtures/CodeGen/` (larger stubs like
      `ScaffoldModule.GenerateCode()`'s full template). Prefer inline strings unless
      output exceeds ~30 lines.

---

## Phase 1 — Pure logic, zero Unity/VRC dependency (EM-Pure)

Highest ROI, no scene/Udon lifecycle needed at all. Do these first.

**Items 1.1–1.5 (CodeGen: `UdonWriter`, `TsvrcModule` helpers, `FactoryModule.Sanitize`,
`PoolModule` slot-count graph, `TranslationModule` parsing) moved to
`CODEGEN_TESTING_PLAN.md` Phase G1/G2 — see that file for their full, expanded
treatment.**

- [x] 1.6 **`Editor/Tools/Translation/TsvrcTranslationWindow.cs` pure helpers** —
      `PeekKeyLabel` (malformed JSON, whitespace tolerance, multiple `"key"`/`"label"`
      occurrences risking wrong-match since regex isn't JSON-aware), `TargetPattern`/
      `KeyRegex`/`LabelRegex` directly, and a regression test that the window's own
      hardcoded sample JSON string parses correctly via `PeekKeyLabel`.
- [x] 1.7 **`Editor/Tools/MeshCombiner/MeshCombinerWindow.cs` — `ComputePathError()`
      and `GetValid()`** — pure string validation / list-dedup logic (make `internal`
      or reflect): empty path, missing `Assets/` prefix, missing `.asset` suffix,
      duplicate/null entries in `_sources`.
- [x] 1.8 **`Runtime/Player/TsPlayer.cs` — `ToArray(string)`** — the one method with
      zero `VRCPlayerApi` dependency; everything else in this file moves to Phase 5.
- [x] 1.9 **`Runtime/Core/WirePoolAttribute.cs`** — trivial reflection test that
      `Description` round-trips (default null vs. explicit value). Low priority.

## Phase 2 — Edit Mode with real Unity objects, no Udon/scene lifecycle (EM-Obj)

Needs `GameObject`/`Texture2D`/`Mesh` instances but not `SendCustomEvent`, not
networking, not a live Play Mode session.

- [ ] 2.1 **`Runtime/Utils/TsArray.cs`** — extend existing `TsArrayTests.cs` in place;
      it already covers `Add`/`Remove`/`Contains` for both `string[]` and
      `UdonSharpBehaviour[]`. Add: destroyed-object "fake null" equality edge case
      (Unity's overridden `==` on a destroyed `UdonSharpBehaviour`), preserving order
      guarantees explicitly.
- [ ] 2.2 **`Runtime/Utils/TsJson.cs`** — confirm `VRCJson` runs outside Play Mode first
      (spike this before writing the suite); then: null/empty input to
      `Deserialize`/`DeserializeToken`, malformed JSON, JSON parsing to a non-dict
      token (array/primitive) rejected by `Deserialize` but accepted by
      `DeserializeToken`, `Clone` round-trip of nested dict/list, unicode/CJK
      round-trip, empty-string chain (`Clone` → `Deserialize("")` → null).
- [ ] 2.3 **`Runtime/UI/Utils/TextureGraphics2D.cs`** — the most Edit-Mode-friendly
      file in the whole Runtime tree; test the `*ToBuffer` methods directly against
      plain `Color32[]` arrays (no `Texture2D` needed): `DrawCircleToBuffer` (center
      at/outside bounds, radius 0/negative), `DrawTriangleToBuffer` (headings
      0/90/180/270°, negative/>360°, degenerate zero half-width/height, edge-pixel
      inclusion consistency), `DrawLineToBuffer`/`DrawHorizontalLineToBuffer`/
      `DrawVerticalLineToBuffer` (Bresenham horizontal/vertical/diagonal, thickness
      0/1/even, out-of-bounds clamps), `ClearBuffer`/`FillBuffer` on zero-length
      buffer, buffer-length-mismatch defensive test (document current
      `IndexOutOfRangeException` behavior). Then the `Texture2D`-based overloads
      (`FillTexture`/`ClearTexture`/`DrawLine`/`DrawCircle`) via `new Texture2D(w,h)`
      + `GetPixels32()` assertions, including the "does not call `Apply()`" contract
      and `FlushBuffer`'s `Apply(false)` no-mipmap-recalc behavior.
- [ ] 2.4 **`Editor/Tools/MeshCombiner/MeshCombinerTool.cs` — `Combine()`** — the
      highest-value Editor-tool target: build small in-memory scene graphs
      (`GameObject`s with `MeshFilter`+`MeshRenderer`+various `Collider`s under a
      root `Transform`) and call `Combine` directly. Cover: no renderable submeshes
      throws `InvalidOperationException`; multiple sources sharing one material →
      single submesh; `sharedMaterials.Length` shorter than submesh count → last
      material reused; null material slot → submesh skipped; `ExcludeEditorOnly`
      filtering; disabled colliders skipped; trigger `MeshCollider` with null
      `sharedMesh` vs. with mesh; non-trigger `MeshCollider` with null `sharedMesh`
      silently skipped (document as intentional or bug); sources with
      `UdonSharpBehaviour` excluded from collider collection entirely; null
      `colliderOnlySources`; null `root` (world space) vs. non-null (local-space
      transform math); exception-path cleanup of temp meshes via `finally`.
- [ ] 2.5 **`Editor/Tools/MeshCombiner/MeshCombinerWindow.cs` non-GUI helpers** —
      `CopyCollider`, `SaveAsset` (AssetDatabase create/overwrite, verify collision
      mesh path string-surgery `_savePath[..^6] + "_Collider.asset"` on edge-case
      paths), `LoadFromSelection` (dedup via `HashSet`s, overlapping hierarchy
      selection). Clean up temp `.asset` files in `TearDown`.

## Phase 3 — Play Mode, single instance, no networking/ownership (PM-Solo)

First tier of Udon-lifecycle-dependent tests. One `GameObject`, `SendCustomEvent`
works, but nothing here depends on `Networking.*`/multi-player state.

- [ ] 3.1 **`Runtime/StateMachine/StateManager.cs`** — attach to a bare GameObject +
      one or two dummy target `UdonSharpBehaviour`s with named enter/exit methods.
      Cover: same-state `SetState` no-ops (no exit/enter/`OnStateChanged`/`TsEmit`);
      initial `SetState` from `_currentState == -1` skips exit; transition to/from
      unregistered state doesn't throw; `RegisterState` with null enter/exit method
      names produces no phantom `SendCustomEvent("")`; `target=null` defaults to
      `this`; `UnregisterState` then transition through the removed state; re-register
      overwrite semantics; strict ordering exit→`OnStateChanged`→`TsEmit`→enter.
- [ ] 3.2 **`Runtime/Core/TsvrcBehaviour.cs`** — pub/sub (`TsSubscribe`/`TsEmit`):
      multiple subscribers to one event (broadcast to all), emit with no subscribers,
      double-subscribe same listener, unmatched `TsEmit` key does nothing, listener
      invocation order, `TsConstruct` called twice (double `TsStart` guard), `TsEmit`
      after `TsDestroy`/GameObject destroyed.
- [ ] 3.3 **`Runtime/Core/TsvrcInstance.cs`** — default `IsTsMaster` passthrough to
      `Networking.IsMaster` in a solo session (expected true); regression guard for
      when custom master logic is later added (marked TODO in source).
- [ ] 3.4 **`Runtime/Timing/TsvrcTimer.cs` — owner-only-solo subset** — start/stop/
      pause/resume/elapsed/remaining math with a single simulated client (server time
      still works solo): double-`StartTimer` guarded no-op; negative duration → 0
      (open-ended); `GetElapsedMilliseconds` clamping (server clock skew/rollback);
      `GetRemainingMilliseconds` with `_durationMs <= 0` → 0; pause-while-paused /
      resume-while-not-paused no-ops; stopping/completing a **paused** timer freezes
      elapsed correctly (no over-count, via `!_isPaused` guard); auto-complete exact
      boundary tick (`< _durationMs` vs `>=`); integer-overflow note for >24.8-day
      durations (document, don't necessarily test). Non-owner `OnDeserialization`
      inference and ownership abandonment move to Phase 5.
- [ ] 3.5 **`Runtime/DataTransfer/DataChunker.cs`** — via a thin test subclass exposing
      the protected methods (`ValidateMessage`, `CreateDataChunks`,
      `CalculateTotalChunks`, `ExtractChunk`): exact multiples of `CHUNK_SIZE=2500`
      (no extra empty chunk), 1-char string, empty/null input rejected by
      `ValidateMessage` before reaching chunking, message length exactly
      `MAX_MESSAGE_SIZE=500000` (valid) vs. `+1` (rejected), last-chunk
      shorter-than-`CHUNK_SIZE` slicing, regression test pinning the `CHUNK_SIZE`
      constant.
- [ ] 3.6 **`Runtime/UI/List/TsListItem.cs`** — solo bind/unbind lifecycle on a bare
      instantiated GameObject with a mock/real `TsList`: `_OnItemPressed` no-ops when
      `!_isBound`/`_list == null` (stale click after `Unbind` race); re-`Bind` while
      already bound fully overwrites state; `Unbind` when never bound is safe;
      `_OnUnbind` runs while `_itemData`/`_dataIndex` are still valid (per doc
      comment); negative/`-1` `dataIndex` defensive test.
- [ ] 3.7 **`Runtime/UI/List/TsList.cs`** — needs a small built prefab (item prefab +
      container `Transform`), still solo/no networking (`BehaviourSyncMode.None`):
      `PageCount` with `_pageSize <= 0` → 1; `SetData(null)` → empty state + cleared
      selection; `SetData` with empty `DataList` → empty state; `SetPage` no-ops
      outside `STATE_POPULATED`; out-of-range page clamps; `NextPage`/`PreviousPage`
      at boundaries no-op; partial last page count math; instantiated prefab missing
      `TsListItem` component → destroyed + null pool slot handled gracefully by
      `_ClearPool`; null `_itemContainer`/`_itemPrefab` → silent no-op; non-dict
      `DataList` entries fall back to empty `DataDictionary`; rapid `SetData`/`SetPage`
      calls don't leak GameObjects (old pool always destroyed before new created).
      (This one technically fits "PM-Scene" — listed here since it's still solo.)

## Phase 4 — Play Mode requiring a built scene/prefab hierarchy, still solo-player (PM-Scene)

- [ ] 4.1 **`Runtime/Player/HeadClipGuard.cs`** — scene with a player rig +
      `BoxCollider`s to bake OBBs. Cover: `Begin(count > array.Length)`/negative count
      clamped via `Mathf.Min`; null colliders in the array skipped; `_batchFrames < 1`
      forced to 1; movement-gate boundary (`_movSkip` `>=`/`<`); symmetric push
      cancellation (`sqrPush <= 1e-8f`) falls back to `_lastSafePlayerPos`; capsule
      already inside OBB (nearest-face push branch); batch swap-with-last self-swap
      guard; re-`Begin()` after `End()` fully resets state; `_playerReady` reset when
      `_localPlayer` transiently invalid. If the math proves hard to reach through
      `PostLateUpdate` alone, consider extracting `ComputePushLocal` to a pure
      testable helper (feeds back into Phase 1/2).
- [ ] 4.2 **`Runtime/UI/TsvrcPlayerPositionOverlay.cs` — solo/local-only subset** —
      Canvas + `RawImage` + the overlay component: `Setup()` invalid args (width/
      height/pixelsPerUnit `<= 0`, null `OverlayImage`) → error + no mutation;
      `Setup()` called while already running → stop+restart with new dimensions, old
      texture destroyed, no duplicate tick loops; `StartOverlay` before `Setup` →
      error no-op; double-tick guard counters; pixel clamping at texture edges;
      `_bufferIsClean` optimization (skip GPU upload when nothing changed); local
      player invalid (`IsValid()` false) still reflects remote-only draws correctly;
      triangle heading math at boundary angles; malformed `_ParsePlayerIntId` input;
      runtime marker-size changes picked up by `_RecacheFields()`; `StopOverlay`
      actually clears texture, still fires `OnOverlayUpdatedEvent` on a no-op clear.
      Multi-player marker resolution moves to Phase 5.
- [ ] 4.3 **`Runtime/Session/RankedGameSession.cs` — local state-machine subset** —
      build the 4-sub-behaviour + `TsvrcRoot`-double hierarchy, single local client:
      `StartSession` rejected when `_masterOnly` and not master; rejected when
      `CurrentState != Idle` (double-start); `StopSession` during `Loading` routes
      through `_readyCheck.StopReadyCheck()` → `_OnReadyCheckStopped` → Idle;
      `_EndSession` re-entrancy guard (`CurrentState != InGame` no-ops second call) —
      test both timer-complete-then-all-leave orderings; `AddLobbyPlayer`/
      `RemoveLobbyPlayer` before `StartLobbyTracking` → error no-op; `_timerDurationMs
      = 0` meaning no-timer-end combined with `_endOnTimerComplete`; `SetTimerDuration`
      must precede `StartSession` to take effect; missing wired sub-references logged
      and defensively handled. Multi-player join/leave/master-gating races move to
      Phase 5.

## Phase 5 — Play Mode, 2+ simulated players / ClientSim (PM-Multi)

The hardest tier — ownership transfer, ordering races between `[NetworkCallable]`
events and `[UdonSynced]` replication, join/leave/suspend handling. Build these last,
on top of the ClientSim harness from Phase 0.

- [ ] 5.1 **`Runtime/Core/TsvrcProcess.cs`** (base class underlying everything below —
      test it directly via a minimal subclass before testing subclasses):
      double `StartProcess` before either client's `RequestSerialization` arrives;
      `OnPlayerLeft` firing when local player isn't owner yet (fallback via
      `OnOwnershipTransferred`); suspended owner tick loop doesn't stall,
      `OnPlayerSuspendChanged` triggers `SetOwner` race resolution among non-suspended
      clients; stale/out-of-order `OnDeserialization` packet from a departed/suspended
      owner overwriting `_ownerId` post-takeover (sentinel guards); inline re-entrant
      `StartProcess` from inside `OnProcessStopped`/`OnProcessCompleted`; rate limiting
      on `RequestStopProcess`/`RequestCompleteProcess` (1/s) and non-owner request
      forwarding; dual-authority check window (`IsProcessOwner()` OR
      `Networking.IsOwner`) during handoff.
- [ ] 5.2 **`Runtime/Tracking/PlayerTracker.cs`** — dedup on `StartPlayerTracking` with
      duplicate input IDs; `AddTrackedPlayers`/`RemoveTrackedPlayers` null/empty
      no-ops; idempotent add (already-tracked ID, no spurious broadcast); remove of
      untracked ID no-ops; `[NetworkCallable]` spoofing guard rejects non-owner direct
      calls (test both the `_isBroadcasting` legitimate bypass and a malicious direct
      call); `OnPlayerLeft` only broadcasts for tracked+owner; `OnPlayerSuspendChanged`
      doesn't double-remove a player who both suspends and leaves;
      `OnOwnerAbandonedProcess` batch-removes departed/suspended players correctly at
      scale (many players); known VRChat `OnOwnershipTransferred`-after-`OnPlayerLeft`
      ordering bug path; late joiner gets accurate `LastPlayerIds` via
      `OnDeserialization` without replayed network events; inline restart-from-callback
      race doesn't clear the newly-started list; rate limits under rapid add/remove.
- [ ] 5.3 **`Runtime/Tracking/AutoPlayerTracker.cs`** — clarify for the user: this is
      **ID-list auto-membership, not per-GameObject autogeneration** — no prefabs are
      instantiated per player. Tests: `StartAutoTracking()` snapshot excludes the
      already-fired `OnPlayerJoined` wave (start after join, not during); player
      joining after start goes through the `OnPlayerJoined` override (owner-only,
      `IsValid()`/`IsProcessRunning()`/`IsProcessOwner()` guards); non-owner client's
      `OnPlayerJoined` does nothing locally (relies on broadcast); `StartPlayerTracking`
      called directly with custom IDs is silently ignored (redirected to full
      snapshot) — explicit regression test since it's a surprising override; owner
      leaving mid-tracking combined with simultaneous new join (ordering race); join
      then immediate leave churn converges correctly; `StopAutoTracking`/
      `CompleteAutoTracking` ignore joins that arrive after stop.
- [ ] 5.4 **`Runtime/Tracking/ReadyCheckProcess.cs`** — `_readyCheckActive` vs.
      `IsProcessRunning()` divergence when `SetReady` is called from inside a network
      event handler; `CheckAllPlayersReady` with zero tracked players never
      auto-completes; last unready player leaving/being removed can complete the
      check for everyone; `SetReady(false)` after ready correctly un-marks without
      re-triggering completion; owner-direct vs. non-owner-network `SetReady` paths;
      `SetReady` by a non-tracked player no-ops; restart-from-`OnReadyCheckCompleted`
      race clears `_readyPlayerIds` before nested `base.OnProcessStarted()`;
      self-only spoofing guard on `BroadcastAddReadyPlayer`/`BroadcastRemoveReadyPlayer`;
      `OnOwnerAbandonedProcess` batch removal of ready+tracked players at scale; late
      joiner derives `_readyCheckActive` from synced `_isRunning`, not a network event.
- [ ] 5.5 **`Runtime/Timing/TsvrcTimer.cs` — multi-player subset** — non-owner
      `OnDeserialization` inferring start/stop/complete/pause/resume purely from
      synced-state transitions; late joiner arriving mid-run-while-paused sees correct
      paused state (`!wasRunning && isRunning` branch also emits paused); `_wasCompleted`
      flag correctly distinguishes natural completion vs. early stop for non-owner
      transition; owner leaving mid-run/mid-pause preserves `_isPaused`/
      `_elapsedOffsetMs` for the new owner; non-owner pause/resume request forwarding
      + 2/s rate limit under rapid toggling.
- [ ] 5.6 **`Runtime/Player/TsPlayer.cs` — multi-player subset** — `FindPlayerByID`
      malformed/non-existent ID returns null; `ToPlayerApis` mixed valid/invalid IDs
      produces correctly-trimmed array (no trailing nulls); display names containing
      `#` don't break the `displayName#playerId` format; `GetAllPlayerIDs()` at 0
      players and near the 82-player cap.
- [ ] 5.7 **`Runtime/Session/RankedGameSession.cs` — multi-player subset** — master
      gating with an actual non-master caller; `_OnPlayersCompleted`
      completed-count-vs-game-count edge (0 game players); `_OnGamePlayersRemoved`
      ends session when last game player leaves; concurrent `StartSession` calls from
      two clients.
- [ ] 5.8 **`Runtime/UI/TsvrcPlayerPositionOverlay.cs` — multi-player subset** —
      remote-player marker resolution/rendering with 2+ players (local vs. remote
      distinguishing logic only exercises meaningfully here); `_ParsePlayerIntId`
      multi-candidate/no-match branches.
- [ ] 5.9 **DataTransfer subsystem — the largest test surface in the library.**
      Build bottom-up; lower classes get focused tests, `DataTransferer` gets
      black-box end-to-end integration tests.
  - [ ] 5.9.1 **`DataSender.cs`** — malicious/direct call to
        `NotifyTrackedPlayersDataTransferStarted/Stopped/Completed` by a non-owner,
        non-broadcasting caller rejected (cover both `caller == null` local-call and
        `owner == null` unowned-object cases); rapid cancel+transfer near the 100/s
        rate limit; new transfer started before a deferred `_EmitDataTransferStopped/
        Completed` fires clears stale pending flags; already-cleared flag emit is a
        no-op.
  - [ ] 5.9.2 **`DataChunkReceiver.cs`** — the security-critical file: chunk arriving
        while `_transferActive == false` dropped; sender-spoofing (`CallingPlayer`
        mismatch, `CallingPlayer == null` direct non-owner call) rejected; local
        player ID absent from `playerIds` ignored; null `playerIds`/`dataChunk`
        guarded; `totalChunks < 1` or `> _maxChunks` (200) rejected (OOM guard);
        `chunkIndex` out of `[1,totalChunks]` rejected; `totalChunks` mismatch across
        chunks within one transfer rejected (first chunk locks the value in);
        duplicate chunk delivery for the same index ignored (no double
        `OnChunkStored`/ACK); reassembly correctness across many chunks including
        **out-of-order arrival** (placed by `chunkIndex`, not arrival order — a
        valuable explicit test); `_maxChunks` boundary at exactly 200 vs. 201.
  - [ ] 5.9.3 **`DataSenderReceiver.cs`** — new transfer starting between
        `OnChunksAssembled` staging and deferred `_EmitDataReceptionCompleted` firing
        suppresses the stale completion and doesn't corrupt `LastData`; same race for
        `_EmitDataReceptionStopped`; ordering guarantee chunk-events-before-completion;
        idempotent no-op when flag already false; `ResetReceiverState` clears all
        `Last*` fields + pending flags together.
  - [ ] 5.9.4 **`ChunkedTransferSession.cs`** — `TransferData` rejected while already
        running or mid-gap (`_pendingNextChunk`); rejected with null/empty
        `playerIds`; rejected on invalid message; re-entrant `TransferData`/
        `CancelDataTransfer` called from inside `OnChunkSequence*` callbacks;
        `CancelDataTransfer` at all 4 distinct windows (idle, mid-chunk, inter-chunk
        gap, post-completion-pre-cleanup); all target players leaving/suspending
        mid-gap stops and broadcasts; all tracked players leaving during an active
        ready check cancels; ownership abandonment during the gap vs. during an
        active chunk (two distinct recovery branches); last-chunk vs. non-last-chunk
        completion branching. The late/stale `OnDeserialization` packet races
        described at length in source comments are the highest-difficulty tests —
        consider driving them via direct, manually-sequenced method calls
        (`OnDeserialization`/`OnOwnerAbandonedProcess` invoked out of "natural" order)
        rather than relying on real network timing.
  - [ ] 5.9.5 **`DataTransferer.cs` — end-to-end integration** — happy path 1-player
        and multi-player; `OnOwnerAbandonedProcess` override does NOT emit a spurious
        "process is not running" warning (regression test per explicit source
        comment); full-size 500,000-char message → exactly 200 chunks;
        `OnTransferChunkEvent`/`LastChunkIndex`/`LastTotalChunks` fire once per chunk
        per receiver; cancellation before any ack vs. after partial acks.

## Phase 6 — CodeGen (moved)

**Entire phase moved to `CODEGEN_TESTING_PLAN.md` Phases G0–G7**, which replaces this
flat 6.1–6.13 checklist with a full architecture writeup (Part 1), an isolation
strategy for testing scene-wiring without depending on the project's real scene/config
(Part 2), an exhaustive test list (Part 3), and a list of candidate bugs/design gaps
found while planning (Part 4). Covers all 8 modules (`ScaffoldModule`,
`SingletonModule`, `ConstructModule`, `FactoryModule`, `InstanceModule`,
`MemoryModule`, `PoolModule`, `TranslationModule`) plus `TsvrcGenerator`,
`TsvrcAssetWatcher`, `TsvrcBuildCompile`, `PackagePaths`, `TsvrcDomainReloadHandler`.

## Phase 7 — Editor tool / window smoke tests (Smoke)

Not meaningfully unit-testable beyond "doesn't throw" — GUI interaction can't be
scripted well. Do these last; low priority, cheap safety net.

- [ ] 7.1 **`Editor/Configure/TsvrcWindow.cs`** — open window with/without a
      `TsvrcConfig` in the test scene, call `OnGUI`, assert no exceptions; multiple
      `TsvrcConfig` instances in scene (document `FindObjectOfType` picks
      arbitrarily); `_tabIndex` out-of-range guard after modules list shrinks.
- [ ] 7.2 **`Editor/Configure/ObjectListGUI.cs`** — construct a dummy
      `SerializedObject` with an object-array field, call `DrawObjectList` inside a
      fake `OnGUI`, assert no exceptions.
- [ ] 7.3 **`Editor/Drawers/ReadOnlyDrawer.cs` / `WirePoolAttributeDrawer.cs`** —
      single smoke test each (near-duplicate implementations): create an object with
      the attributed field, call `GetPropertyHeight`/`OnGUI`, assert `GUI.enabled` is
      restored afterward even if an exception occurs inside `PropertyField` (flag the
      missing try/finally as a real bug worth fixing, not just testing around).
- [ ] 7.4 **`Editor/Tools/MeshCombiner/MeshCombinerWindow.cs` — `OnGUI`/`Draw*`
      methods** — open window, repaint with empty sources / invalid path /
      prefab-instance particle systems, assert no exceptions. (Non-GUI helpers
      already covered in Phase 2.)
- [ ] 7.5 **`Editor/Tools/Translation/TsvrcTranslationWindow.cs` — `OnGUI` flow** —
      open with/without a config, 0/1/many language files, assert no exceptions;
      verify `OnEnable`/`OnDisable` correctly (un)subscribe from
      `EditorApplication.hierarchyChanged`/`projectChanged` across repeated open/close
      cycles (no leaked static handlers). (Pure regex helpers already covered in
      Phase 1.)

## Phase 8 — Manual-only gates (Manual)

Cannot be automated; keep as a checklist run before each publish, per `TESTING.md`
Layer 4.

- [ ] 8.1 VRChat SDK "Build & Test" with Number of Clients ≥ 2 — real compiled Udon
      bytecode, real networking, sync vars, ownership transfer, actual VM behavior —
      final gate for anything touched in Phase 5 (DataTransfer subsystem especially).
- [ ] 8.2 `ShowCreateSampleDialog` in `TsvrcTranslationWindow.cs` — uses
      `EditorUtility.SaveFilePanel`, a native OS dialog; manual click-through only.
- [ ] 8.3 Full VCC install/update flow for ClientSim and any future VRChat SDK
      package bumps — GUI-only, not scriptable.
- [ ] 8.4 Visual/UX check of `TsvrcPlayerPositionOverlay` and `TsList` in a real
      headset session — automated tests validate data/pixel correctness, not
      actual on-headset legibility/perf.

---

## Coverage checklist (every class, cross-referenced)

Use this to confirm nothing was missed once phases are complete.

**Runtime/Core**: TsvrcBehaviour (3.2), TsvrcInstance (3.3), TsvrcProcess (5.1),
TsvrcRoot (covered indirectly as test-double base, 0.5), WirePoolAttribute (1.9).

**Runtime/Config**: TsvrcConfig, TsvrcFactoryGroup — pure data containers, no
behavior; used only as fixtures across Phase 6 tests, no dedicated test needed.

**Runtime/Utils**: TsArray (2.1), TsJson (2.2), TsMemory (5.x — see note below),
ReadOnlyAttribute (no test needed, marker only).

  - [ ] **Note:** `TsMemory.cs` wasn't assigned a phase number above — add it to
        Phase 5 as **5.10**: ephemeral tier (Register/Set/Get/type-coercion/Remove/
        Clear guard rails) testable PM-Solo; persist tier needs `OnPlayerRestored`
        firing (ClientSim persistence support); synced tier replication needs 2+
        players. Edge cases: `Add`/`Set` before `Register` falls to ephemeral;
        double-`Register` on same key errors; `Register` with both/neither
        persist+synced rejected; `Set` on synced key when not owner triggers
        `SetOwner`; `Add` on network-populated synced key silently skips (no error,
        unlike ephemeral/persist); `GetInt`/`GetFloat` coercion from `Double` after a
        JSON/PlayerData round-trip; `_WriteToPd` before `_playerRestored` warns, value
        lost; `OnPlayerRestored` for a key with missing `_types` entry warns, restore
        skipped; PlayerData's 100KB-per-player limit silently drops (stress test if
        feasible); `Remove` on unregistered/persistent key messaging; `Clear()`
        no-op fast path when `_syncedStore.Count == 0`; malformed sync JSON payload on
        `OnDeserialization` returns early without emitting.

**Runtime/DataTransfer**: DataChunker (3.5), ChunkedTransferSession (5.9.4),
DataSender (5.9.1), DataChunkReceiver (5.9.2), DataSenderReceiver (5.9.3),
DataTransferer (5.9.5).

**Runtime/Player**: HeadClipGuard (4.1), TsPlayer (1.8 + 5.6).

**Runtime/Session**: RankedGameSession (4.3 + 5.7).

**Runtime/StateMachine**: StateManager (3.1).

**Runtime/Timing**: TsvrcTimer (3.4 + 5.5).

**Runtime/Tracking**: AutoPlayerTracker (5.3), PlayerTracker (5.2),
ReadyCheckProcess (5.4).

**Runtime/UI**: TsList (3.7), TsListItem (3.6), TsvrcPlayerPositionOverlay
(4.2 + 5.8), TextureGraphics2D (2.3).

**Editor/CodeGen**: entire subtree (TsvrcGenerator, TsvrcModule, UdonWriter,
PackagePaths, TsvrcAssetWatcher, TsvrcBuildCompile, TsvrcDomainReloadHandler, all 8
modules, TsvrcBuiltinConfig/TsvrcTranslationConfig) — see `CODEGEN_TESTING_PLAN.md`,
Phases G0–G7, for the full breakdown.

**Editor/Configure, Drawers, Tools**: ObjectListGUI (7.2), TsvrcWindow (7.1),
ReadOnlyDrawer/WirePoolAttributeDrawer (7.3), MeshCombinerTool (2.4),
MeshCombinerWindow (2.5 + 7.4), TsvrcTranslationWindow (1.6 + 7.5).

If every row above has a checked-off phase item, coverage is complete for this
plan's scope. Line-by-line correctness verification of each test's assertions
happens during Phase implementation, not during this planning pass.
