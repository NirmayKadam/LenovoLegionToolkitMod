# Bug fixes

Bugs found in a code review of Lenovo Legion Toolkit v2.36.0.0 and fixed in this fork. Each fix comes with automated tests where the behaviour can be tested without Legion hardware.

## Automation

### Scheduled triggers fired on the wrong day
The automation UI saves trigger *times* in UTC but the selected *days* as local days, while matching used the UTC day. In India (UTC+5:30), "Monday 02:00" is stored as 20:30 UTC on Sunday, so it fired on Tuesday. Time ranges crossing local midnight checked the wrong day too.

**Fix:** all matching runs in local time. Stored UTC times (and sunrise/sunset) are converted to local before comparing, and the saved settings format is unchanged. Daylight-saving shifts are out of scope.
Files: `Lib/Extensions/TimeExtensions.cs`, `Lib.Automation/Pipeline/Triggers/Time*.cs`, `TimeTriggerMatching.cs`, `IAutomationEvent.cs`.

### Triggers could miss their second
The time listener ticked every ~1 s and triggers need an exact second match. Timer drift sometimes skipped a second.
**Fix:** the listener catches up on skipped seconds (up to 5 s; bigger gaps such as sleep emit only the current second). The periodic trigger now uses the event's own second.
Files: `Lib/AutoListeners/TimeAutoListener.cs`, `PeriodicAutomationPipelineTrigger.cs`.

### Scripts that print a lot froze all automations
`CMD.RunAsync` redirected output but only read it after the process exited, and never read stderr. Once a script wrote more than the ~4 KB pipe buffer it blocked forever, while holding the automation lock. This was reproduced before the fix.
**Fix:** stdout and stderr are read while the process runs; output isn't redirected when not waiting.
File: `Lib/System/CMD.cs`.

### "Check if already running" false positives
It used `ScriptPath.Contains(processName)`, so anything under `C:\Windows\System32\` counted as running (the `System` process always exists), and case differences were missed.
**Fix:** compare the executable name exactly, ignoring case; dispose `Process` objects.
File: `Lib.Automation/Steps/RunAutomationStep.cs`.

### Cancellation never cancelled
A new event was supposed to interrupt the running automation, but the cancel happened only after acquiring the run lock, when the previous run had already finished.
**Fix:** the run in progress is cancelled before waiting for the lock, and queued runs are never dropped. An interrupted step is no longer reported as a failure.
Files: `Lib.Automation/AutomationProcessor.cs`, `Pipeline/AutomationPipeline.cs`.

### Exceptions could crash the app
Automation event handlers are `async void`; an exception escaping them ends the process. Event processing now catches and logs.

## CLI / IPC

- **Replies over 1 KB were corrupted.** The reader ignored the byte count and decoded the whole 1 KB buffer each time, appending stale bytes; multi-byte characters split across chunks were garbled. Now it reads exact byte counts, decodes once, detects a disconnected client and caps message size. (`CLI.Lib/Extensions/PipeStreamExtensions.cs`)
- **One bad client stopped the server.** An exception while replying ended the server loop. Now each connection is isolated, the pipe is recreated per connection, and a client that never sends times out after 5 s. (`WPF/CLI/IpcServer.cs`)

## Settings

- **Atomic writes.** Settings were written in place, so a crash mid-write could leave a broken file, which then fell back to defaults. They are now written to a temp file and swapped in with one rename. (`Lib/Settings/AbstractSettings.cs`)

## Smaller fixes

- **Smart Fn Lock:** left and right Ctrl/Shift/Alt are tracked separately. On exit, the app waits for any in-flight Fn-lock change before restoring the state.
- **God Mode:** removed unreachable patterns in the fan-table mapping (no behaviour change, locked by a test); the capability-error warning resets on each read; fan-table errors keep their inner exception; the magic `55` became the capability's first step value.
- **Sensors:** CPU base clock no longer fails on CPUs with more than 32 threads (the buffer was capped at 32 entries).

## Build

The unchanged v2.36.0.0 source no longer built: its `LLT.*` NuGet packages used floating versions (`*-*`), and newer releases removed APIs the code uses. All five are pinned to the newest versions published before the v2.36.0.0 release (2026-09-08). (`Lib/LenovoLegionToolkit.Lib.csproj`)

## Known, not changed

- **NonGaming GPU cTGP:** values are written as an offset (`value - 55`) but reads aren't converted back the same way. Whether the BIOS returns absolute or offset values needs a NonGaming device to confirm.
- A few sync-over-async `.Result` calls in UI code remain; they read data that is cached at startup and showed no bug.
