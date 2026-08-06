# Tsvrc.Testing.Framework

Shipped as part of the `com.tsvrc.core` UPM package. Any world built on tsvrc can add
`Tsvrc.Testing.Framework` to a test assembly's `references` and use what's below to test
its own `UdonSharpBehaviour`s, without needing to work around the VRC SDK's Play Mode
quirks by hand. It has no dependency on `Tsvrc.Runtime` - nothing here is tied to tsvrc's
own classes.

## Opt-in contract

Nothing in this assembly does anything to your project just by being compiled. Every
piece of behavior only activates when you actually use it:

- `PrivateFieldAccess` is a set of static methods - calling one does exactly that call,
  nothing more.
- `TsPlayModeTestBase` and its ClientSim/fixup machinery only run when a test class in
  your project actually extends `TsPlayModeTestBase`. If nothing in your domain does,
  the fixups never touch anything, including VRCSDK internals - see
  `UnityEventFilterAllowlistFixup.cs` for how it checks this before doing anything.

If you only reference this assembly for `PrivateFieldAccess`, your project sees zero
other side effects from it.

## What's here

- **`PrivateFieldAccess`** - reflection helpers (`SetField`/`GetField`/`InvokeStatic`/
  `InvokeInstance`) for reaching private fields and methods on any object or static type
  from a test. Standalone, works in Edit Mode or Play Mode, no base class required.

- **`TsPlayModeTestBase`** - extend this for real, ClientSim-backed Play Mode tests
  instead of hand-rolling ClientSim setup/teardown. Exposes `Players`
  (`ClientSimPlayerEnvironment`) for spawning/removing/finding real `VRCPlayerApi`
  instances, `StartClientSim(...)` to begin a session, and `BuildTsRoot<TRoot>()` to
  compose your project's generated composition root from code (see below).

- **`ClientSimPlayerEnvironment`** - the real-player scene/session plumbing
  `TsPlayModeTestBase` wraps. Usable directly if you need finer control than the base
  class gives you.

- **`TsRootBuilder<TRoot>`** - builds a project's generated composition root (`TsGenerated`
  and friends) with `AddComponent` instead of loading a saved scene, then drives the same
  `_TsLogStart`/`_TsMemoryStart`/`_TsSingletonStart`/`_TsPoolStart`/`_TsConstructStart`/
  `_TsInstanceStart` sequence a real client only gets from Unity dispatching `Start()` on a
  scene-loaded object. Reached via `TsPlayModeTestBase.BuildTsRoot<TRoot>()`, which tracks
  and tears down everything the builder creates automatically. Not tied to
  `Tsvrc.Runtime`'s `TsRoot` type - the bootstrap methods are invoked by name via
  reflection, so this stays usable even by a test assembly that doesn't reference
  `Tsvrc.Runtime`. See its own doc comment for the full rationale (in short: `AddComponent`
  objects are plain C#, never compiled to Udon bytecode, so there's no VM to gate
  `Start()`/`SendCustomEvent` dispatch the way there is for a saved scene's baked-in
  UdonBehaviours - the same reason every other PlayMode test in this repo composes its
  subjects the same way instead of loading a scene). Example:

  ```csharp
  public class MyManagerTests : TsPlayModeTestBase
  {
      [UnityTest]
      public IEnumerator MyManager_DoesTheThing()
      {
          yield return StartClientSim();

          var builder = BuildTsRoot<TsGenerated>();
          var myManager = builder.WithNew<MyManager>("MyManager");
          // Configure myManager's own fields here if TsStart() reaches them unconditionally
          // (e.g. UI Button/Text references) before Build() runs.
          builder.Build();

          myManager.DoTheThing();
          // Assert...
      }
  }
  ```

- **`IPlayModeEnvironmentFixup` / `FixupRegistry`** - the catalogue of known VRCSDK/Unity
  Test Framework Play Mode testing defects and their workarounds, applied automatically
  at the right lifecycle point for any class extending `TsPlayModeTestBase`. Disable one
  (e.g. once a future SDK version fixes it upstream) with
  `FixupRegistry.Disable<TFixup>()`, without forking this framework.

## What's not here

`ProcessTestBase` (reflection-seeding helpers for `Process` subclasses) lives in this
repo's own `Tests/EditMode/Core/Process/`, not here. It reflects into `Process`'s
private fields - tsvrc's own implementation details, not a stable contract - so it stays
scoped to this repo's own test suite instead of shipping as public API.

## Setting up tests in a world project that consumes tsvrc

Tsvrc's own generated code (`TsGenerated.cs` and friends, wherever `TsConfig`'s output
folder points) declares the base types your world scripts derive from (`TsBehaviour`,
`TsInstance`, `TsStateManager`, `TsListItem`), and in turn references concrete types from
your own scripts (a `Singleton`/`Construct`/`Factory` entry is generated as a field typed
as whatever world class you registered). That's a two-way dependency, so your world
scripts and your generated output folder must compile into **one** assembly - Unity does
not allow two assembly definitions to reference each other. Give your world's own
`Assets/<YourWorld>` folder a single asmdef (e.g. `<YourWorld>.Runtime`) placed high
enough in the folder tree to cover both your hand-written scripts and wherever
`TsConfig`'s generated-folder setting points (they don't need to be nested inside each
other, just both under the asmdef's root), referencing at least `Tsvrc.Runtime` plus
whatever VRC SDK/UdonSharp assemblies your scripts use. See `Assets/MoL.Runtime.asmdef`
in this repo for a real example - it sits at the project's `Assets/` root specifically so
it covers both `Assets/MoL` and `Assets/TsGenerated` in one assembly.

From there, add your own test assemblies (EditMode and/or PlayMode) referencing your
world's runtime asmdef plus `Tsvrc.Runtime` and, for PlayMode/ClientSim tests, this
assembly (`Tsvrc.Testing.Framework`) - see `Assets/MoL/Tests/EditMode/
MoL.Tests.EditMode.asmdef` and `Assets/MoL/Tests/PlayMode/MoL.Tests.PlayMode.asmdef` for
the exact reference lists to mirror. Every fixup/helper above becomes available
immediately; nothing else needs registering.
