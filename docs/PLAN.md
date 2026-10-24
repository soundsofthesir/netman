# NetMan plan

A Windows tool that stores network adapter profiles, applies one with a single click, and proves the change took effect.

## Requirements

- Profiles belong to an adapter. Each has a name, notes, a mode (DHCP or static), IP, subnet mask, gateway, optional DNS 1 and DNS 2, and an optional ping target.
- Apply sets the adapter in one click, then verifies every value against the live adapter.
- Duplicate a profile on the same adapter.
- Copy a profile to a different adapter.
- Capture the adapter's live settings into a new profile.
- Undo the last apply.
- Minimal UI. Labels and values only, no helper text.

## How a person does this by hand

1. Open `ncpa.cpl`, right-click the adapter, choose Properties.
2. Select Internet Protocol Version 4, click Properties.
3. Choose "Use the following IP address", type IP, mask and gateway, click OK twice.
4. Check with `ipconfig`, ping the device.
5. Reverse it later by choosing "Obtain an IP address automatically".

The command-line equivalents, which the tool will run:

```
netsh interface ipv4 set address name="Ethernet 2" static 192.168.0.50 255.255.255.0 192.168.0.1
netsh interface ipv4 set address name="Ethernet 2" source=dhcp
netsh interface ipv4 set dnsservers name="Ethernet 2" static 192.168.0.1 primary validate=no
netsh interface ipv4 set dnsservers name="Ethernet 2" source=dhcp
```

All need an elevated process. Typical pain: six dialogs per change, retyping the same addresses on every site visit, forgetting the old settings, and not knowing whether the change worked until a ping fails.

## Similar tools

| Tool | Notes | What to take |
|---|---|---|
| NetSetMan | Profiles as tabs, many extra settings. Free version limits the profile count. | The apply model. Drop the extras. |
| Simple IP Config | Open source (AutoIt). Profile list, adapter dropdown, fields, Apply. | Field set and Capture. |
| Argon Network Switcher, IP Shifter | Profile lists, tray menus, DNS and proxy options. | Tray quick-apply later. |
| Windows Settings | One adapter at a time, no profiles, no verification. | The gap this tool fills. |

None of them verify the result beyond the command's exit code.

## UI (direction E, adapter first)

One window, about 720 x 440. Mockups: https://claude.ai/artifact/AxrNtysEyhHYxfKaqLKboc

- Left: adapters, each with its live address or "Disconnected". Refreshes on network change events and every 2 seconds.
- Below the adapters: Reset to DHCP. Sets the selected adapter's address and DNS to automatic and runs the same verify.
- Right: the selected adapter's profiles, each row with name, address and its own Apply button.
- A filled dot marks the profile that matches the adapter's live settings. The tool computes it from the live config every refresh, so it clears if anything else changes the adapter.
- Below the profiles: the result of the last apply, one check per value, with Undo.
- Buttons: New, Edit, Duplicate, Copy to (menu of the other adapters), Capture, and Delete set apart.
- Edit opens a small dialog with all fields. Static selected enables IP to DNS. Mask accepts `255.255.255.0` or `/24`.
- Errors in the dialog: red field border, reason in a tooltip.

## Apply and verify

Every apply runs the same pipeline. Each step reports into the result panel.

1. **Pre-check.** Adapter present by GUID. Input valid (contiguous mask, gateway inside the subnet, no network or broadcast address). IP not already on another local adapter.
2. **Snapshot.** Record the adapter's current config for Undo.
3. **Set.** Run netsh for the address, then DNS. Check each exit code and keep stdout.
4. **Verify.** Poll the live config every 250 ms for up to 10 seconds until it matches:
   - DHCP flag matches the mode.
   - IP present with the right prefix length.
   - Default gateway matches, or none.
   - DNS servers match, when set.
   - Duplicate address detection state is Preferred. Duplicate shows as "Address in use on the network".
   - DHCP mode: a lease that is not 169.254.x.x, waiting up to 15 seconds.
   - Disconnected adapter: compare the stored config in the registry instead, and report "Saved, adapter disconnected".
5. **Reach.** If the profile has a ping target, send 3 pings with a 1 second timeout and report the round trip.
6. **Result.** Applied, Partial, or Failed, with the time. Failed and Partial show which value differs (expected and actual). Undo restores the snapshot and runs the same verify.

Every apply writes the commands, their output and each check to `%APPDATA%\NetMan\log.txt`.

## Technical approach

- C# on .NET 10 (LTS), WinForms, one self-contained `NetMan.exe`, built from macOS with `EnableWindowsTargeting`.
- App manifest with `requireAdministrator`.
- Adapters and live config: `NetworkInterface` and `GetIPProperties()`, keyed by interface GUID. DAD state from `UnicastIPAddressInformation.DuplicateAddressDetectionState`. Stored config for disconnected adapters from `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}`. Virtual and hidden adapters are filtered out using the `Characteristics` flags under the network adapter device class key.
- Apply: `netsh` with an argument list, no shell string, using the adapter's current name resolved from its GUID. Parse exit codes, not text, because netsh output is localized.
- Profiles: `%APPDATA%\NetMan\profiles.json`, written to a temp file then renamed. Each profile stores adapter GUID plus name. If neither matches, the profile shows under "Not found" and Copy to moves it to a present adapter.

### Projects

```
src/NetMan.Core/          Model, validation, store, netsh builder, verifier, process runner, ping, log,
                          registry and NetworkInterface mapping (cross-platform, tested on macOS)
src/NetMan.App/           WinForms UI and Windows-only code: adapter reader, registry access, watcher, elevation
tests/NetMan.Core.Tests/  xUnit
```

The verifier compares an expected config to a live config snapshot through an interface, so tests run on macOS with fakes.

## Phases

1. **Core.** Model, validation, CIDR conversion, JSON store, netsh builder, Duplicate and Copy to, verifier comparison. Tests first.
2. **Adapter layer.** Live config reader, registry reader, process runner, ping, change events, log file.
3. **Apply pipeline.** Pre-check, snapshot, set, verify loop, reach, Undo.
4. **UI.** Main window, edit dialog, result panel, Copy to menu.
5. **Packaging.** Manifest, icon, single-file publish, GitHub Actions build on `windows-latest`.
6. **Windows test pass.** Static, DHCP, blank gateway, DNS set and blank, disconnected adapter, renamed adapter, USB adapter removed, duplicate IP on the network, DHCP with no server, change made outside the tool, Undo, non-admin launch.

Later: tray menu for quick apply, import and export.

## Risks

- **High.** No Windows machine in this environment. Phases 2 to 6 need a Windows box (Tower1 or a VM).
- **Medium.** Windows can take a few seconds to settle an address. The verify loop waits, and the timeouts are constants to tune during testing.
- **Medium.** Adapter names change. GUID matching covers the same device; Copy to covers a different one.
- **Low.** Antivirus can flag unsigned elevated exes. Code signing is optional later.

## Decisions

1. Stack: C# WinForms.
2. DNS: optional primary and secondary. Blank leaves DNS as it is in static mode, and sets it to automatic in DHCP mode.
3. Tray quick-apply menu: later.
4. Layout: direction E, adapter first.

## Complexity

Medium. Core 4 hours, adapter layer 3, apply pipeline 3, UI 5, packaging and Windows testing 4.
