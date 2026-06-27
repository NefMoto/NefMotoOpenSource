# Release workflow

How NefMoto releases are tagged, built, and published. Changelog text comes from [git-cliff](https://git-cliff.org) using [`cliff.toml`](../cliff.toml) at the repo root.

## Overview

1. Tag the commit you want to release (`v*` format below).
2. Push the tag — **`release.yml`** builds the app, creates the MSI, generates release notes, and publishes a GitHub release.
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

Tag the commit on the branch you intend to ship (usually `main` or your release branch). CI checks out that tag, builds Release, runs tests, builds the WiX MSI, and creates the GitHub release.

### 3. Verify on GitHub

Open **Actions → Release** for the workflow run, then **Releases** for the published asset and notes.

To rebuild an existing tag without moving it, use **Actions → Release → Run workflow** (`workflow_dispatch`) and enter the tag name. The workflow checks out that ref and re-runs the full release job.

## CI workflows

| Workflow          | Triggers                               | What it does                                      |
|-------------------|----------------------------------------|---------------------------------------------------|
| **`build.yml`**   | Push/PR to any branch; push of any tag | Build and test; MSI artifact on tag push          |
| **`release.yml`** | Push of `v*` tags; `workflow_dispatch` | Build, test, MSI, release notes, GitHub release   |

`build.yml` retains MSI workflow artifacts for 90 days; `release.yml` is what publishes the installer on GitHub Releases.

### What `release.yml` does

1. **Determine tag type** — Tags with a hyphen suffix (e.g. `-rc1`) are pre-releases. Finds `prev_tag` (nearest ancestor tag from the tagged commit’s parent, often the prior RC) for RC changelog ranges.
2. **Build** — `make release`, `make test CONFIG=Release`, `make installer` (WiX 6).
3. **Release notes** — [git-cliff](https://git-cliff.org) via `kenji-miyake/setup-git-cliff@v2` (version not pinned):
   - **Stable:** `git cliff -l` — changelog since the previous stable tag (`tag_pattern` in `cliff.toml`).
   - **Pre-release:** `git cliff $prev_tag..$tag` — commits since the previous tag on the line (often the prior RC). CI replaces git-cliff’s first line so the header reads `(since $prev_tag)`; git-cliff alone still prints the last **stable** tag there.
4. **Publish** — Appends a `## Downloads` section with the MSI link, then creates the GitHub release via `softprops/action-gh-release@v3`.

CI does **not** pass `--offline` to git-cliff so PR titles, issue links, and contributor metadata can resolve through the GitHub API.

## Changelog configuration

Commit grouping, conventional-commit parsing, and the release-note template live in [`cliff.toml`](../cliff.toml). Highlights:

- **`tag_pattern`** — Only stable four-part tags (`v1.9.6.0`). RC tags are excluded from `-l` / latest-stable logic.
- **Commit parsers** — Conventional prefixes (`feat`, `fix`, `doc`, …) plus legacy message heuristics; unmatched commits go to **Other**.
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
