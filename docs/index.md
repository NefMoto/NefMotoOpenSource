# Docs index

Start with [Getting started](user-guide/getting-started.md). If something fails, use [Troubleshooting](user-guide/troubleshooting.md).

Project overview, features, and requirements: [README.md](../README.md).

## User guides

- [Getting started](user-guide/getting-started.md) — install, cable, first connect, bench vs in-car, DTCs, logs
- [Flashing](user-guide/flashing.md) — KWP read/write, layouts, checksums
- [P0602](user-guide/clearing-P0602.md) — same fault as `P1681`: the stored flag, and what clears it
- [Bootmode](user-guide/bootmode.md) — bootstrap connect, flash auto-detect, physical 95040 (immo, lockout, `P0602` flag)
- [Troubleshooting](user-guide/troubleshooting.md) — symptom index, FAQ, what to attach on a bug report

## Developer / maintainer

- [KWP2000.md](KWP2000.md) — bench results, connect timing, RAM kernel after write, settings persistence
- [ME7-K-line-addresses.md](ME7-K-line-addresses.md) — slow-init and fast-init addresses per ME7 image, from firmware analysis
- [P1681-P0602.md](P1681-P0602.md) — regression guide for `P0602` on a `4B0906018CH`: measurements, the `NL` kernel logic, the ASM listing, and the diagrams
- [BUILDING.md](BUILDING.md) — build from source
- [RELEASE.md](RELEASE.md) — how releases are tagged and published

## Links

- [GitHub issues](https://github.com/NefMoto/NefMotoOpenSource/issues)
- [Discussion thread](https://nefariousmotorsports.com/forum/index.php?topic=12861.0)
- [Latest release](https://github.com/NefMoto/NefMotoOpenSource/releases/latest)
