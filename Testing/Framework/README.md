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
  instances, and `StartClientSim(...)` to begin a session.

- **`ClientSimPlayerEnvironment`** - the real-player scene/session plumbing
  `TsPlayModeTestBase` wraps. Usable directly if you need finer control than the base
  class gives you.

- **`IPlayModeEnvironmentFixup` / `FixupRegistry`** - the catalogue of known VRCSDK/Unity
  Test Framework Play Mode testing defects and their workarounds, applied automatically
  at the right lifecycle point for any class extending `TsPlayModeTestBase`. Disable one
  (e.g. once a future SDK version fixes it upstream) with
  `FixupRegistry.Disable<TFixup>()`, without forking this framework.

## What's not here

`TsProcessTestBase` (reflection-seeding helpers for `TsProcess` subclasses) lives in this
repo's own `Tests/EditMode/Core/TsProcess/`, not here. It reflects into `TsProcess`'s
private fields - tsvrc's own implementation details, not a stable contract - so it stays
scoped to this repo's own test suite instead of shipping as public API.
