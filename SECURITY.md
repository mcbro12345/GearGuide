# Security Policy

## Supported Versions

Only the latest released version of Gear Guide receives security fixes. Update to the newest release before reporting an issue, in case it is already fixed.

## Reporting a Vulnerability

Report security vulnerabilities privately using GitHub's [private vulnerability reporting](https://github.com/mcbro12345/GearGuide/security/advisories/new), not a public issue. This keeps details out of view until a fix is available.

Include:

- The affected Gear Guide version and Dalamud/API version
- Steps to reproduce
- The potential impact

Expect an initial response within a few days. Confirmed vulnerabilities will be fixed and disclosed once a patched release is available.

## Scope

Gear Guide is a client-side Dalamud plugin that runs inside your own FINAL FANTASY XIV process. It does not run a server or accept network connections. With Market Board and Live Listings on, it sends item IDs and your data center's name to `universalis.app`. Relevant reports include things like unsafe memory access, crashes triggered by malformed game or Universalis data, equipping items the player did not choose, or sending data beyond what the README's Privacy section describes.
