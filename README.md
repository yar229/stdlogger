# StdLogger

A small .NET CLI tool that runs a command (or reads stdin) and routes its **stdout/stderr through Serilog**, assigning a log level to every line based on configurable regular expressions.

Logs can be written to the console and/or to dated files with daily rolling, while each line's level (`Fatal`…`Verbose`) is detected from its content — e.g. a line containing `[ERROR]` is logged as `Error`.

## What it does

- Runs a child process and streams its output to Serilog sinks (console, file, etc.).
- Reads stdin line-by-line if no command is given.
- Detects the log level of each line using regex patterns (matched from `Fatal` down to `Verbose`, first match wins).
- Writes an informational header before logging starts, showing the command and the configured sinks/options, e.g.:

  ```
  [20:05:35 INF] StdLogger for cmd /c echo [ERROR] boom; sinks: Console, File -> D:\Logs\log-20260911.log (rolling: Day); min level: Verbose
  ```

## Requirements

- .NET 10 SDK to build.

## Usage

```
StdLogger --path <dir> [command args...]
StdLogger --config <config.json> [command args...]
```

One of `--path` or `--config` is required; they cannot be combined. Every argument after the option is treated as the command to run.

> **Note on Windows shells:** `cmd /c` must be used for shell constructs like `&&`, since `StdLogger` launches an executable without a shell, e.g.
> `StdLogger --path C:\logs cmd /c "echo [ERROR] boom && echo [WARNING] careful"`

### Run a command

```cmd
stdlogger --path D:\logs cmd /c "dir && echo [WARNING] something"
```

```cmd
stdlogger --config my-settings.json myapp.exe --flag1 --flag2
```

### Read stdin

Without a command, each line read from stdin is logged:

```cmd
myapp.exe 2>&1 | stdlogger --path D:\logs
```

## Options

| Option       | Description                                                                                                 |
|--------------|-------------------------------------------------------------------------------------------------------------|
| `--path <dir>`    | Log to `<dir>` using the **default** `appsettings.json` that sits next to the executable. File sinks are redirected into `<dir>`. Relative `<dir>` is resolved against the executable's directory. |
| `--config <file>` | Use the given JSON file for all Serilog settings **and** level patterns. Relative file-sink paths are resolved against the config file's directory. |

## Output order

The child's **stdout and stderr are read in parallel**. Both streams keep their identity, so a stderr line that matches no level pattern is logged as `Error` — but the interleaving **order between the two streams is not deterministic** (a trailing `2>&1` line may show up before earlier stdout lines, or vice versa).

If you need the log to preserve the **exact chronological order** of the output, append `2>&1` to the command you run, so the child merges stderr into stdout itself:

```cmd
stdlogger --path D:\logs myapp.exe 2>&1
```

Lines then arrive in the same order they would appear in a console. Keep in mind that in this case stderr can no longer be distinguished, so lines matching no level pattern are logged as `Information` (not `Error`).

### stdbuf (unbuffered output)

To reduce ordering skew while **keeping the stdout/stderr split**, you can make the child's C-runtime output unbuffered with `stdbuf` (ships with Git for Windows / MSYS2 / Cygwin). A buffered child flushes stdout in one burst on exit, which makes those lines race ahead of earlier stderr lines; unbuffered output avoids that final burst, so lines arrive closer to the moment they were written:

```bash
stdlogger --path D:\logs stdbuf -oL -eL myapp.exe
```

`-oL` / `-eL` make stdout/stderr line-buffered; `-o0` / `-e0` disable buffering entirely.

Limitations: `stdbuf` only works for programs linked against the MSYS2/Cygwin runtime (the tools shipped with Git Bash / MSYS2 / Cygwin). It cannot hook a native `cmd.exe` batch or an ordinary Windows executable, it does not guarantee exact cross-stream ordering, and the streams stay separate — so a stderr line that matches no level pattern is still logged as `Error`.

## What gets logged

- **Level detection**: each output line is matched against the patterns in `StdLogger:LevelPatterns`. Patterns are tried in priority order — `Fatal` first, then `Error`, `Warning`, `Information`, `Debug`, `Verbose` — and the **first match wins** (a line containing both `[WARNING]` and `[ERROR]` is logged as `Error`).
- **No match**: lines from stderr default to `Error`, lines from stdout to `Information` (unless you merged the streams yourself with `2>&1`, see [Output order](#output-order)).
- **Command starting fails**: an `Error` event is logged and exit code `1` is returned.
- Otherwise the exit code of the child process is returned (`0` on success).

## Configuration

### Default config (`--path` mode)

`appsettings.json` is copied next to the executable at build time. It is standard Serilog JSON configuration with two sinks:

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": { "Default": "Verbose" },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": { "path": "log-.log", "rollingInterval": "Day" } }
    ]
  },
  "StdLogger": {
    "LevelPatterns": [ ... ]
  }
}
```

With `--path D:\logs`, the file sink writes to `D:\logs\log-20260911.log` (daily rolling).

### Custom config (`--config` mode)

Any Serilog JSON config works. Relative `path` values in File sinks are resolved **relative to the config file's directory** (not the executable), so a config stays portable regardless of where it is launched from. Absolute paths are used as-is.

### Level patterns

`StdLogger:LevelPatterns` is an array of `{ "Level": ..., "Pattern": ... }`. If the section is missing or empty, built-in defaults are used.

Default patterns (regex, case-insensitive):

| Level | Purpose  | Pattern                                        |
|-------|----------|------------------------------------------------|
| Fatal | Fatal    | `]? FATAL (\s*\w{2}\d+)? (\]|\:)`             |
| Fatal | Critical | `]? CRITICAL (\s*\w{2}\d+)? (\]|\:)`          |
| Error | Error    | `]? ERROR (\s*\w{2}\d+)? (\]|\:)`             |
| Warning | Warning | `]? WARN(?:ING)? (\s*\w{2}\d+)? (\]|\:)`     |
| Information | Info | `]? INFO (\s*\w{2}\d+)? (\]|\:)`             |
| Debug | Debug    | `]? DEBUG (\s*\w{2}\d+)? (\]|\:)`             |
| Verbose | Trace   | `]? TRACE (\s*\w{2}\d+)? (\]|\:)`             |

Notes:

- Patterns are compiled with `IgnoreCase | IgnorePatternWhitespace`, so unescaped spaces inside a pattern are ignored.
- The optional `\s*\w{2}\d+` part matches things like machine/cabinet names (`MB503`, `RD10`) appearing after the level token.
- The trailing `(\]|\:)` anchors the level token to `]` or `:` so bare words such as `Information` in regular prose are not misdetected.
- Level names map to Serilog levels: `FATAL`/`CRITICAL` → `Fatal`, `ERROR` → `Error`, `WARN`/`WARNING` → `Warning`, `INFO`/`INFORMATION` → `Information`, `DEBUG` → `Debug`, `TRACE`/`VERBOSE` → `Verbose`.

In JSON, backslashes must be escaped, e.g.:

```json
{ "Level": "Error", "Pattern": "\\]? ERROR (\\s*\\w{2}\\d+)? (\\]|\\:)" }
```

### Multiple patterns per level

You can list several patterns for the same level — all are tried before moving to the next (lower) level. This is how the defaults support both `FATAL` and `CRITICAL`.

## Examples

Detect and log levels from a command, writing into `D:\logs`:

```cmd
stdlogger --path D:\logs cmd /c "echo [ERROR] test failed && echo [WARNING] retrying && echo done"
```

Output (console sink):

```
[20:05:35 INF] StdLogger for cmd /c echo [ERROR] test failed && echo [WARNING] retrying && echo done; sinks: Console, File -> D:\logs\log-20260911.log (rolling: Day); min level: Verbose
[20:05:35 ERR] [ERROR] test failed
[20:05:35 WRN] [WARNING] retrying
[20:05:35 INF] done
```

Same lines are appended to `D:\logs\log-20260911.log` (rolling daily → `log-20260912.log` tomorrow).

## Exit codes

- `0` — success (stdin mode, or the child command exited `0`).
- `1` — missing/invalid options, failed to configure the logger, or the child command failed to start.
- otherwise — the child process's own exit code.