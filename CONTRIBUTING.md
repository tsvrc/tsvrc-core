# Contributing to TsVRC

## Setup

- **Unity 2022.3**, matching `package.json`'s manifest target.
- Open this repo as a Unity project (it's a VPM package, not a full project on its own,
  so you'll typically want it installed into a test world project via the [VRChat Creator
  Companion](https://vcc.docs.vrchat.com/) rather than opened standalone). See [Add TsVRC
  to your project](https://tsvrc.com/docs/tsvrc/add-to-your-project) for the general
  install flow, and point VCC at a local checkout of this repo instead of the published
  package if you're testing a change.

## Running tests

Tests live under `Tests/EditMode` and `Tests/PlayMode`, already wired into their own
assemblies referencing `Tsvrc.Runtime`/`Tsvrc.Editor`. Open **Window > General > Test
Runner** in Unity and run the EditMode and PlayMode suites from there. See [Testing your
world](https://tsvrc.com/docs/tsvrc/testing/testing-your-world) for background on the
testing helpers themselves (`TsPlayModeTestBase`, `PrivateFieldAccess`, ClientSim).

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
