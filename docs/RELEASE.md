# Release workflow

How NefMoto releases are tagged, built, and published. Changelog text comes from [git-cliff](https://git-cliff.org) using [`cliff.toml`](../cliff.toml) at the repo root.

## Overview

1. Tag the commit you want to release (`v*` format below).
2. Push the tag — **`release.yml`** builds the Windows MSI, builds self-contained `ecuflash` tar.gz archives for Linux and macOS, generates release notes, and publishes a GitHub release.
3. Optionally preview the changelog locally with `git cliff` before tagging (see [Preview changelog locally](#preview-changelog-locally)).

Pushing a branch is not enough; the release workflow runs only on **tag push** (or manual dispatch).

## Tag formats

| Tag format  | Example        | GitHub release                            |
|-------------|----------------|-------------------------------------------|
| Stable      | `v1.9.6.0`     | Full release                              |
| Pre-release | `v1.9.6.1-rc1` | Marked pre-release (`-rc`, `-beta`, etc.) |

Stable tags match `tag_pattern` in `cliff.toml` (`^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$`). RC and other suffix tags do not.

## Cut a release

### 1. Preview (optional)

See [Preview changelog locally](#preview-changelog-locally) to check what will appear in the release notes.

### 2. Tag and push

**Release candidate:**

```shell
git tag v1.9.6.1-rc1
git push origin v1.9.6.1-rc1
```

**Stable release:**

```shell
git tag v1.9.6.1
git push origin v1.9.6.1
```

Tag the commit on the branch you intend to ship (usually `main` or your release branch). CI checks out that tag, builds Release, runs tests, builds the WiX MSI and four `ecuflash` tar.gz archives, and creates the GitHub release.

### 3. Verify on GitHub

Open **Actions → Release** for the workflow run, then **Releases** for the published asset and notes.

To rebuild an existing tag without moving it, use **Actions → Release → Run workflow** (`workflow_dispatch`) and enter the tag name. The workflow checks out that ref and re-runs every release job. CI deletes any existing GitHub release for that tag first, then publishes a fresh one (needed when a tag was deleted and recreated).

## CI workflows

| Workflow          | Triggers                                         | What it does                                                        |
|-------------------|--------------------------------------------------|---------------------------------------------------------------------|
| **`build.yml`**   | Push/PR to any branch; push of any tag           | Build and test on Windows. MSI smoke on **PRs** and **pushes to the default branch** (artifact kept 14 days). No MSI on tags. `ecuflash` publish smoke on Ubuntu for `linux-x64` and `osx-arm64` (no artifact upload). |
| **`release.yml`** | Push of `v*` tags (stable and RC); `workflow_dispatch` | Build, test, MSI, Unix `ecuflash` tar.gz archives, release notes, GitHub release |

`release.yml` publishes the Windows installer and four self-contained `ecuflash` tar.gz archives on GitHub Releases. `build.yml` does not upload release assets on tag push.

The generated **Downloads** section lists the MSI (with the .NET 10 Desktop Runtime requirement) and the Linux/macOS tar.gz archives (self-contained; no separate .NET install). Keep that in sync with README **Requirements**. The MSI is **x64** and installs to Program Files; upgrading from an older x86 MSI should uninstall the Program Files (x86) copy.

### What `release.yml` does

1. **Prepare** — Tags with a hyphen suffix (e.g. `-rc1`) are pre-releases. Finds `prev_tag` (nearest ancestor tag from the tagged commit’s parent, often the prior RC) for RC changelog ranges.
2. **Windows MSI** — On `windows-latest`: `make release`, `make test CONFIG=Release`, `make installer` ([`Installer/installer.ps1`](../Installer/installer.ps1): local WiX from [`.config/dotnet-tools.json`](../.config/dotnet-tools.json) via `dotnet tool restore` / `dotnet tool run wix`). Uploads the MSI as a workflow artifact.
3. **Unix tar.gz** — On `ubuntu-latest`, matrix over `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`: `make ecuflash RID=…` (self-contained single-file `ecuflash` plus `MemoryLayouts`, packed as `ecuflash-<tag>-<rid>.tar.gz`). Cross-RID publish from Linux; no macOS runner. Uploads each archive as a workflow artifact.
4. **Release notes** — [git-cliff](https://git-cliff.org) via [`taiki-e/install-action@v2`](https://github.com/taiki-e/install-action) (`tool: git-cliff` in `release.yml`, unpinned). The tool version follows that action’s manifests (cooldown of a day or so), not a live GitHub release every job. Dependabot only updates the action, which is how cliff moves. **Blind spot:** there is no PR smoke for notes without churning temporary tags. An RC job that fails to generate notes (including a future 3.0) is the smoke; fix `cliff.toml` or the notes bash before the full tag. Do not add a fake-tag notes job.
   - **Stable:** `git cliff -l` — changelog since the previous stable tag (`tag_pattern` in `cliff.toml`).
   - **Pre-release:** `git cliff $prev_tag..$tag` — commits since the previous tag on the line (often the prior RC). CI replaces git-cliff’s first line so the header reads `(since $prev_tag)`; git-cliff alone still prints the last **stable** tag there.
5. **Publish** — Downloads the MSI and tar.gz artifacts, deletes any existing GitHub release for the tag (re-tag / re-run), appends a `## Downloads` section (Windows MSI + four Unix archives), then creates the release via `softprops/action-gh-release@v3`.

CI does **not** pass `--offline` to git-cliff so PR titles, issue links, and contributor metadata can resolve through the GitHub API.

## Changelog configuration

Commit grouping and the release-note template live in [`cliff.toml`](../cliff.toml). Highlights:

- **`tag_pattern`** — Only stable four-part tags (`v1.9.6.0`). RC tags are excluded from `-l` / latest-stable logic.
- **Commit parsers** — `feat` and `Add` (Features), `fix` (Bug Fixes), `doc` / `docs` (Documentation), `refactor` (Refactor), `chore`, `ci`, `cosmetic`, and `style` (Chore), `deps` and `chore(deps)` (Dependencies). `Bump` subjects and Dependabot merge commits are Dependencies. Identical `Bump` subjects collapse to one line. Non-Dependabot merge commits are skipped. Unmatched commits go to **Other**.
- **Template** — Prefers GitHub PR title when available; links `#123` to issues.

## Preview changelog locally

Use these before tagging to review or paste into a draft. Pass **`--offline`** locally for reproducible output without GitHub API calls (CI omits this on purpose).

**Unreleased work** (everything since the last stable tag, including RC commits):

```shell
git cliff --offline --unreleased
```

**Stable release** (what `-l` produces after you tag):

```shell
git cliff --offline -l
```

**Release candidate** (incremental since the prior tag on the line; RC tags are outside `tag_pattern`):

```shell
prev=v1.9.6.1-rc2
tag=v1.9.6.1-rc3
git cliff --offline "${prev}..${tag}" | { read -r _; echo "## What's Changed (since ${prev})"; cat; }
```

Use the previous tag on the release line — the last RC, or the last stable tag for the first RC of a version (e.g. `v1.9.6.0..v1.9.6.1-rc0`). The `{ read …; echo …; cat; }` block matches CI’s header fix; omit it only if you want git-cliff’s default `(since <last stable>)` label.
