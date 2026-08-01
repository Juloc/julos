# Release notes template

Copy this file for a release, replace every placeholder and remove sections that do not apply. A section that applies must not be left empty; write "None" instead, so a reader can tell the difference between "nothing changed" and "nobody checked".

---

# JulOS `<version>`

Released `<YYYY-MM-DD>`.

## Summary

One paragraph describing what this release changes for the person running JulOS.

## Upgrade

| Question | Answer |
|---|---|
| Minimum previous version | `<version>` |
| Database migration required | yes / no |
| Migration reversible | yes / no, with the limit |
| Downtime expected | `<duration>` |
| Safe mode required | yes / no |

Steps that differ from the standard upgrade runbook:

1. …

## Breaking changes

Each entry names what breaks, who is affected and what to do instead.

- …

## Added

- …

## Changed

- …

## Fixed

- …

## Security

Fixed security issues, their severity and whether an installation was exposed by default.

- …

## Package compatibility

| Package | Version | Requires core |
|---|---|---|
| … | … | … |

## Artifacts

Every artifact is referenced by an immutable version or digest. No `latest` reference is published, as decision `D020` requires.

| Artifact | Reference | Digest |
|---|---|---|
| `julos-server` | `<version>` | `sha256:…` |

## Known limitations

Behaviour a user could reasonably expect that this release does not provide, with the issue that tracks it.

- …
