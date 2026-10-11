# Security policy

## Reporting a vulnerability

Don't open a public issue. Use
[GitHub private vulnerability reporting](https://github.com/tsvrc/tsvrc-core/security/advisories/new)
for this repository instead, so the report isn't visible before a fix is available.

If that's not accessible to you for some reason, contact
[@ToniSeas](https://github.com/ToniSeas) directly.

## Scope

This covers the Core code: `Runtime/`, which ships in world builds, and `Editor/`, which runs in the
Unity Editor and writes generated code into the project.

It doesn't cover the VRChat platform, the VRChat Worlds SDK, UdonSharp or Udon Test Kit. Report the
VRChat ones to [VRChat support](https://hello.vrchat.com/), and the kit ones as its
[security policy](https://github.com/tsvrc/udon-test-kit/blob/main/SECURITY.md) describes.

## Supported versions

Only the latest release is supported. There's no backport policy for older versions.

## Response

This project is maintained by one person in their spare time. There's no guaranteed response time,
but security reports get priority over other issues.
