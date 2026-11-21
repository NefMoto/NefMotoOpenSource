# Status message sinks

Local developer note for `StatusMessageType` and where each value goes.

## Types

| Type | Meaning |
| --- | --- |
| `ERROR` | Failures that must surface |
| `PROGRESS` | Long-op start, percent, outcome |
| `USER` | Status-pane narrative |
| `LOG` | Diagnostic detail for the log file |
| `DEV` / `DEV_USER` | Debug-build detail |
| `TRACE` | Per-frame / protocol traffic |

## GUI (`ECUFlasher`)

| Type | Status pane | `NefMoto.log` |
| --- | --- | --- |
| `ERROR` | yes | yes |
| `PROGRESS` | yes | yes |
| `USER` | yes | yes |
| `LOG` | no | yes |
| `DEV` / `DEV_USER` | no | Debug builds only |
| `TRACE` | no | no (`Debug.Print` in Debug) |

## CLI (`ecuflash`)

| Type / helper | Stream | Default | `-v` | `-vv` | `-vvv` | `-d` |
| --- | --- | --- | --- | --- | --- | --- |
| `Result` (product lines) | stdout (or `-o` only) | yes | yes | yes | yes | yes |
| `Error` / `ERROR` | stderr | yes | yes | yes | yes | yes |
| `Progress` / `PROGRESS` | stderr | yes | yes | yes | yes | yes |
| Prompts | stderr | yes | yes | yes | yes | yes |
| `USER` | stderr | no | yes | yes | yes | (with `-v`) |
| `LOG` | stderr | no | no | yes | yes | (with `-vv`) |
| `DEV` / `DEV_USER` | stderr | no | no | no | yes | (with `-vvv`) |
| `TRACE` | stderr | no | no | no | no | yes |

`--help` / `-V` use stdout. Bad-arg usage uses stderr.
