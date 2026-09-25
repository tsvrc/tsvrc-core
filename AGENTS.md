# AGENTS.md

## Project overview

UdonSharp framework for VRChat worlds (Unity 2022.3, VPM package `com.tsvrc.core`).
Runtime code lives in `Runtime/`, editor/codegen tooling in `Editor/`, tests in `Tests/`.

## Setup

- Requires Unity 2022.3, opened via the VRChat Creator Companion (VCC) pointing at a
  local checkout, not opened standalone. See `CONTRIBUTING.md`.

## Test

- Open **Window > General > Test Runner** in Unity and run the EditMode and PlayMode
  suites, or on Windows run `Scripts~/Invoke-UnityTests.ps1`. See `CONTRIBUTING.md`.

## Code style

- Fields are private by default. If something needs to be public, expose it as a global
  rather than widening the field's own access (see `Editor/CodeGen` history for the
  pattern).
- Never hand-edit generated code. If generated output looks wrong, the fix belongs in
  `Editor/CodeGen`.

## Commit / PR rules

- Every commit must be signed off (`git commit -s`) per the
  [DCO](https://developercertificate.org/).
- Disclose AI assistance in the PR description. You're responsible for reviewing and
  testing everything you submit, the same as if you'd written it by hand. See
  `CONTRIBUTING.md`.

## Security

- Don't commit secrets or API tokens. Report vulnerabilities per `SECURITY.md`, not as a
  public issue.
