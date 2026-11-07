# Testing

## Automated tests

The `LenovoLegionToolkit.Tests` project (xUnit) has **170 tests**. Run them with:

```powershell
dotnet test LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

They need Windows (they use real Windows APIs) but no Legion hardware and no administrator rights.

| Area | Folder | What is covered |
|---|---|---|
| Automation | `Automation/` | Local-time trigger matching (incl. UTC+5:30 and midnight-crossing ranges), skipped-second catch-up, pipeline cancellation with real steps, process-name matching |
| CLI | `CLI/` | Pipe messages from 100 B to 200 KB and multi-byte text over real named pipes, client disconnects, per-user pipe name, server identity check, `FirstPipeInstance` |
| Security | `Security/` | Folder ACL checks, plugin discovery, settings migration and permissions, JSON type allow-list (including a live gadget control test), backup inspection, Lenovo signature checks, locked downloads, autostart location, boot-logo GIF validation and re-encoding, certificate validation |
| System | `System/` | Running processes that write large stdout/stderr |
| Settings, Utils, Controllers | | Atomic writes, Authenticode verification (valid / tampered / unsigned), fan-table mapping, CPU base clock |

## Testing rules used in this fork

1. **Real inputs, no fakes.** Tests use real files, folders, ACLs, named pipes, child processes and signed binaries. There is no mock that just returns the expected answer.
2. **A test is never weakened to make it pass.** When a test failed, the cause was found first, and the evidence is recorded in the test where relevant. In several cases the test itself was wrong (byte offsets, pipe ordering, an encoder that hadn't finished writing); those were fixed without changing what is asserted.
3. **Negative tests prove their reason.** Where a test checks that something is rejected, a paired test shows the same input passes once only that one limit is relaxed, or a control test shows the attack really works without the fix.

## Manual checklist (needs a Legion/LOQ laptop)

These can only be checked on real hardware with an installed (Program Files), elevated build. **None have been completed yet.**

- [ ] First start migrates settings to `Program Files\LenovoLegionToolkit\UserData\<SID>`, and all settings (God Mode presets, automations, keyboard) are still there
- [ ] A DLL placed in `%LOCALAPPDATA%\LenovoLegionToolkit\Plugins` is **not** loaded and the one-time notice appears
- [ ] A time trigger set ~2 minutes ahead on today's day fires once, at the right local time
- [ ] CLI (enable it in Settings → Integrations): `llt quickAction --list` works, including with many or non-English quick-action names; killing a client mid-request doesn't break the next command
- [ ] A "Run program" step that prints a lot (with "wait") finishes, and later automations still run
- [ ] Restoring a backup containing a "Run program" automation shows the warning
- [ ] Driver update check (Packages page) still works and lists updates
- [ ] Custom boot logo: a static image and an animated GIF both show at boot
- [ ] Autostart can be enabled from the installed copy and starts LLT at logon; a portable copy shows the "not allowed" message
- [ ] God Mode, power modes, fan curves, Fn Lock and keyboard lighting work as before
