# Security policy

## Reporting a vulnerability

Don't open a public issue. Report it through
[GitHub private vulnerability reporting](https://github.com/tsvrc/tsvrc-core/security/advisories/new),
which keeps the report private until a fix is out. If you can't use it, email
[contact@tsvrc.com](mailto:contact@tsvrc.com).

Say what the vulnerability is, how to reproduce it, which versions it affects, and how to fix or
work around it if you know.

## What happens next

You'll get a reply within 7 days. Once the report is confirmed, I prepare the fix in a private
security advisory, then publish the advisory with the release that fixes it, crediting you unless
you'd rather not be named. A vulnerability is made public within 90 days of the report, fixed or
not, so users can protect themselves.

## Scope

This covers the Core code: `Runtime/` and `Prefabs/`, which ship in world builds, and `Editor/`,
which runs in the Unity Editor and writes generated code and scene references into the project.

It doesn't cover the VRChat platform, the VRChat Worlds SDK, UdonSharp or Udon Test Kit. Report the
VRChat ones to [VRChat support](https://hello.vrchat.com/), and the Udon Test Kit ones as its
[security policy](https://github.com/tsvrc/udon-test-kit/blob/main/SECURITY.md) describes.

## Supported versions

Only the latest release gets security fixes.
