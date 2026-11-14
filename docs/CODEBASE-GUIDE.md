# Understanding this codebase

A practical path from "I don't know what language this is" to "I can find and change things". It assumes you can program a little, but not in C# or for Windows.

The full source is in this repository, so you don't need to *reverse-engineer* the app. You read it, run it and step through it. The reverse-engineering in this project went into Lenovo's undocumented hardware interfaces (WMI methods, driver codes); [§ 6. The hardware side](#6-the-hardware-side) explains how to explore those safely.

The companion document [ARCHITECTURE.md](ARCHITECTURE.md) is the reference map. This one is the tour.

## 1. The tech stack in plain words

| Thing | What it is | Where you see it |
|---|---|---|
| **C#** | The programming language. Statically typed, object-oriented, garbage-collected; looks like Java or TypeScript. | Every `.cs` file |
| **.NET 9** | The runtime and standard library C# runs on (like the JVM for Java). `dotnet` is its command-line tool. | `*.csproj`, `dotnet build` |
| **Project / solution** | A `.csproj` builds one program or library (a "DLL"). The `.sln` groups them. | `LenovoLegionToolkit.sln` |
| **NuGet** | .NET's package manager (like npm or pip) | `<PackageReference>` in `.csproj` |
| **WPF** | Windows Presentation Foundation: Microsoft's desktop UI framework. Screens are described in **XAML** (an XML dialect), and behavior lives in a matching "code-behind" `.xaml.cs` file. | `*.xaml` + `*.xaml.cs` in `LenovoLegionToolkit.WPF` |
| **WPF-UI** | A library of Windows 11-style controls for WPF (cards, navigation, toggle switches) | `xmlns:wpfui=...` in XAML |
| **Dependency injection (Autofac)** | Instead of `new`-ing its dependencies, a class asks for them in its constructor, and a "container" supplies shared instances | `IoCModule.cs`, `IoCContainer.Resolve<T>()` |
| **async/await** | How C# waits for slow work (hardware, disk) without freezing the UI | `async Task`, `await` everywhere |
| **WMI** | Windows Management Instrumentation: a Windows API for querying and calling into hardware and system components. Lenovo's BIOS exposes its gaming controls as WMI classes. | `Lib/System/Management/WMI.*.cs` |
| **P/Invoke** | Calling native Windows (C) functions from C#. This repo generates the bindings with CsWin32. | `PInvoke.CreateFile(...)`, `NativeMethods.txt` |
| **xUnit** | The test framework | `LenovoLegionToolkit.Tests` |

### C# features you'll meet (and what they mean)

```csharp
public class PowerModeFeature(GodModeController godModeController, ...)   // primary constructor: parameters become fields
    : AbstractWmiFeature<PowerModeState>(...)                             // inherits a generic base class

public async Task<PowerModeState[]> GetAllStatesAsync()                   // async method returning an array later
{
    var mi = await Compatibility.GetMachineInformationAsync()             // wait without blocking
                                .ConfigureAwait(false);                   // "don't come back to the UI thread"
    ...
}

string? name;                         // ? = may be null (nullable reference types are on)
state is PowerModeState.Quiet or PowerModeState.Balance   // pattern matching
var lockOn = hardwareState switch { FnLockState.On => true, _ => false };   // switch expression
$"Power mode: {state}"                // string interpolation
items.Where(x => x.Enabled).ToArray() // LINQ: query collections with lambdas
[Display(Name = "...")]               // attribute: metadata read at runtime (here: display names)
```

### WPF in five minutes

- A **window** or **page** is a `.xaml` file (layout) plus a `.xaml.cs` file (code). `x:Name="_comboBox"` in XAML becomes a field `_comboBox` in the code.
- **Resources** are named values (colors, styles) looked up by key. `{DynamicResource TextFillColorPrimaryBrush}` means "use whatever that key holds now, and update if it changes". That's how themes and this mod's styles recolor the app live.
- **Styles** set many properties at once; **templates** replace how a control is drawn.
- Only the **UI thread** may touch controls. Code on other threads uses `Dispatcher.Invoke(...)`.
- This codebase builds many controls in C# rather than XAML (e.g. `AbstractToggleFeatureCardControl`). That's normal WPF too.

## 2. Tools

| Tool | Why |
|---|---|
| **.NET 9 SDK** (installed) | Builds and runs everything |
| **Visual Studio 2022 Community** (free), workload ".NET desktop development". *Or* JetBrains Rider (free for non-commercial use). | Best way to read this code: **Go to Definition** (F12), **Find All References** (Shift+F12), the debugger with breakpoints, and a XAML preview. |
| VS Code with the C# Dev Kit extension | Lighter alternative; good navigation, weaker WPF support |
| PowerShell | Exploring WMI (read-only commands below) |
| Git | Seeing history: `git log -p -- path/to/file` |

Open `LenovoLegionToolkit.sln` in Visual Studio. It loads all projects; the LampArray packaging project also builds there.

## 3. Reading order

Each step names the files to open. Use Go to Definition to follow anything interesting.

**Step 1: the shape (15 min).** Read [ARCHITECTURE.md](ARCHITECTURE.md) down to "The core patterns". Glance at `Directory.Build.props` and each `*.csproj`.

**Step 2: how it starts (30 min).** Open `LenovoLegionToolkit.WPF/App.xaml.cs` and read `Application_Startup`. The method names tell the story: flags → container → compatibility → hardware init → background services → main window. Then open `LenovoLegionToolkit.Lib/IoCModule.cs` to see every class the container knows about.

**Step 3: one feature, bottom to top (45 min).** Follow Fn Lock through its five files (the worked example below). This single path teaches the pattern used by about 60 features.

**Step 4: the power mode, the most important feature (45 min).** Read these in order:
1. `Lib/Features/PowerModeFeature.cs`;
2. `Lib/Features/AbstractWmiFeature.cs`;
3. `Lib/System/Management/WMI.LenovoGameZoneData.cs`;
4. `Lib/System/Management/WMI.cs`, the generic `CallAsync`, `ReadAsync` and `Listen`.

Then the event side:
1. `Lib/Listeners/PowerModeListener.cs`;
2. `Lib/Listeners/AbstractWMIListener.cs`.

Compare with walkthroughs 1 and 2 in ARCHITECTURE.md.

**Step 5: the UI around it (1 h).**
1. `WPF/Windows/MainWindow.xaml`: the navigation rail and the frame.
2. `WPF/Pages/DashboardPage.xaml.cs`: how the cards are built.
3. `WPF/Extensions/DashboardItemExtensions.cs`: dashboard item → card.
4. `WPF/Controls/AbstractComboBoxFeatureCardControl.cs` and `AbstractToggleFeatureCardControl.cs`: the generic cards.

**Step 6: knowing the laptop (30 min).** `Lib/Utils/Compatibility.cs` and `MachineInformation` in `Lib/Structs.cs`. Search the code for `mi.Properties.` to see how features adapt to each model.

**Step 7: automation (1 h).**
1. `Lib.Automation/Pipeline/AutomationPipeline.cs`;
2. `Lib.Automation/Pipeline/Triggers/IAutomationPipelineTrigger.cs`, plus one trigger such as `PowerModeAutomationPipelineTrigger.cs`;
3. `Lib.Automation/Steps/IAutomationStep.cs`, `AbstractFeatureAutomationStep.cs`;
4. `AutomationProcessor.cs`, the part that ties events to pipelines.

**Step 8: the rest, as needed.**
- God Mode: `Lib/Controllers/GodMode/`.
- Sensors: `Lib/Controllers/Sensors/`.
- Keyboards: `Lib/Controllers/*Keyboard*`, `Lib/System/*DeviceFactory.cs`.
- Driver/BIOS updates: `Lib/PackageDownloader/`.
- CLI: `CLI/Program.cs` → `WPF/CLI/IpcServer.cs`.
- Settings storage: `Lib/Settings/AbstractSettings.cs`, `Lib/Utils/Folders.cs`.

## 4. Watch it run

Reading goes faster when you can watch the code run.

**Run with logging and the debug console.** The Debug build runs without admin rights (`app.debug.manifest`):

```powershell
dotnet build LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj -c Debug
& "BuildLLT\bin\Debug\net9.0-windows10.0.26100.0\win-x64\Lenovo Legion Toolkit.exe" --trace --debug
```

- `--trace` writes `%LOCALAPPDATA%\LenovoLegionToolkit\log\log_<date>.txt`. Almost every method logs what it does ("Setting state to On… [feature=FnLockFeature]"), so the log is a timeline of the code path.
- `--debug` opens a console with startup progress, and makes WMI-based features report "supported" so you can explore UI that your laptop lacks. Values still come from the real hardware, so expect errors on features the laptop doesn't have.
- Without admin rights some hardware calls fail. Read the log to see which and why.

**Use the debugger.** In Visual Studio, set `LenovoLegionToolkit.WPF` as the startup project and choose the Debug configuration. Under project properties → Debug, add `--trace` as a command-line argument. Then put a breakpoint in `FnLockFeature.ToInternalAsync`, press F5, and toggle Fn Lock on the dashboard. You'll stop inside the feature with the whole call stack visible: card → generic control → feature.

**Run the tests:**

```powershell
dotnet test LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

Tests are small, runnable examples of how classes are meant to be used. `Tests/UI/MotionTests.cs` and `StatusStripTests.cs` are easy starting points.

**Exercises that build understanding:**
1. Find every place that publishes a `NotificationMessage` (Find All References on its constructor). Which events show pop-ups?
2. Add a `Log.Instance.Trace` line in `PowerModeListener.OnChangedAsync`, run with `--trace`, press Fn+Q, and find your line in the log.
3. Find where `HasQuietToPerformanceModeSwitchingBug` is set and where it's used. What does the workaround do?
4. List which settings JSON files exist in your settings folder, and match each to its `AbstractSettings` class.
5. Trace `llt feature set fn-lock on` from `CLI/Program.cs` to `FnLockFeature`.

## 5. Worked example: how Fn Lock is wired end to end

This is the template for adding a feature. Fn Lock touches every layer with almost no extra logic.

**1. The states:** `Lib/Enums.cs`. The display names come from `Lib/Resources/Resource.resx` (and its hand-maintained `Resource.Designer.cs`).

```csharp
public enum FnLockState
{
    [Display(ResourceType = typeof(Resource), Name = "FnLockState_Off")] Off,
    [Display(ResourceType = typeof(Resource), Name = "FnLockState_On")]  On
}
```

**2. The feature:** `Lib/Features/FnLockFeature.cs`. It talks to Lenovo's energy driver:

```csharp
public class FnLockFeature() : AbstractDriverFeature<FnLockState>(Drivers.GetEnergy, Drivers.IOCTL_ENERGY_SETTINGS, useDriverQueue: true)
{
    protected override uint GetInBufferValue() => 0x2;                               // the read request the driver expects
    protected override Task<uint[]> ToInternalAsync(FnLockState s) => ... [0xE] or [0xF];   // lock on / off codes
    protected override Task<FnLockState> FromInternalAsync(uint state) => ... bit 10 of the reply;
}
```

The base class does the `DeviceIoControl` call, the locking and the logging. The feature only knows the numbers. (Newer Legions invert the meaning; `TranslateStateAsync` handles that from `MachineInformation`.)

**3. Registration:** `Lib/IoCModule.cs`:

```csharp
builder.Register<FnLockFeature>();   // also available as IFeature<FnLockState>
```

**4. The dashboard card:** `WPF/Controls/Dashboard/FnLockControl.cs`:

```csharp
public class FnLockControl : AbstractToggleFeatureCardControl<FnLockState>
{
    protected override FnLockState OnState => FnLockState.On;
    protected override FnLockState OffState => FnLockState.Off;
    public FnLockControl() { Icon = SymbolRegular.Keyboard24; Title = Resource.FnLockControl_Title; ... }
}
```

The generic base finds `IFeature<FnLockState>` in the container, hides itself if the feature isn't supported, and calls `GetStateAsync`/`SetStateAsync`. The card also refreshes when `SpecialKeyListener` reports the Fn Lock key.

To make it a dashboard item:
1. Add a value to `DashboardItem` (`WPF/Enums.cs`).
2. Give it an icon, a title and a control in `WPF/Extensions/DashboardItemExtensions.cs`.
3. Optionally, add it to the default groups (`DashboardGroup.DefaultGroups`, `WPF/Structs.cs`).

**5. The command line:** `WPF/CLI/Features/FeatureRegistry.cs`:

```csharp
new FeatureRegistration<FnLockState>("fn-lock"),   // → llt feature set fn-lock on
```

**6. Automation:** `Lib.Automation/Steps/FnLockAutomationStep.cs`:

```csharp
public class FnLockAutomationStep(FnLockState state) : AbstractFeatureAutomationStep<FnLockState>(state)
{
    public override IAutomationStep DeepCopy() => new FnLockAutomationStep(State);
}
```

To show it in the editor:
1. Create a control `WPF/Controls/Automation/Steps/FnLockAutomationStepControl.cs`.
2. Map the step to it in `WPF/Controls/Automation/AutomationPipelineControl.cs`.
3. Add `new FnLockAutomationStep(default)` to the "add step" list in `WPF/Pages/AutomationPage.xaml.cs`.

Saved pipelines may contain any LLT type, so the settings type allow-list needs no change.

**7. Tests:** add what you can test without the laptop to `LenovoLegionToolkit.Tests` (state translation, formatting). Hardware calls need the manual checklist in [TESTING.md](TESTING.md).

## 6. The hardware side

LLT controls the laptop through interfaces Lenovo doesn't document. The upstream developers worked them out from Lenovo Vantage, Legion Zone and the BIOS. In this code they're named, so you can read what's known.

| Channel | Where in code | What it is |
|---|---|---|
| **WMI, `root\WMI` namespace** | `Lib/System/Management/WMI.Lenovo*.cs` | Classes like `LENOVO_GAMEZONE_DATA` (power mode, GPU, fans), `LENOVO_OTHER_METHOD` (capability features), `LENOVO_FAN_METHOD`, `LENOVO_CPU_METHOD` (God Mode limits), lighting classes, and events like `LENOVO_GAMEZONE_SMART_FAN_MODE_EVENT` |
| **EnergyDrv driver** | `Lib/System/Drivers.cs` | Lenovo's kernel driver at `\\.\EnergyDrv`. LLT sends it `DeviceIoControl` codes (`IOCTL_ENERGY_SETTINGS`, `IOCTL_ENERGY_BATTERY_CHARGE_MODE`, …) for battery mode, Fn Lock, keyboard and so on. |
| **UEFI variables** | `Lib/Features/AbstractUEFIFeature.cs` | Firmware settings stored in NVRAM (e.g. Flip to Start) |
| **USB HID** | `Lib/System/RGBDeviceFactory.cs`, `SpectrumDeviceFactory.cs`, `Lib/Utils/HidUtils.cs` | Feature reports to the keyboard controllers |
| **NVIDIA NVAPI** | `Lib/System/NVAPI.cs` | dGPU state, processes, overclock |
| **PawnIO + LibreHardwareMonitor** | `Lib/Controllers/Sensors/*SensorProvider.cs`, `Lib/Utils/PawnIOHelper.cs` | Detailed sensors through a signed kernel driver |

**Exploring WMI safely.** These PowerShell commands only **read**. Run them in an elevated PowerShell on the Legion:

```powershell
# Which Lenovo classes does this laptop have?
Get-CimClass -Namespace root/WMI | Where-Object CimClassName -like 'LENOVO*' | Select-Object CimClassName

# What methods does the GameZone class offer?
(Get-CimClass -Namespace root/WMI -ClassName LENOVO_GAMEZONE_DATA).CimClassMethods | Select-Object Name, Parameters
```

**Don't call `Set…` methods by hand.** They change firmware behavior: fans, power limits, charging. Calling one with wrong values can misbehave or harm the hardware. Use the app, or read how the app calls them.

`Probe.exe` (build `LenovoLegionToolkit.Probe`) runs a structured scan of what a laptop supports. The upstream project uses it when adding new models.

## 7. Glossary

| Term | Meaning |
|---|---|
| **EC** | Embedded controller: the small chip that runs fans, battery charging and keyboard lights. The BIOS's WMI methods mostly talk to it. |
| **GameZone** | Lenovo's name for its gaming WMI interface (`LENOVO_GAMEZONE_*`) |
| **Smart fan mode / power mode** | Quiet, Balance, Performance (and Extreme, Custom), switched with Fn+Q |
| **God Mode / Custom mode** | User-set CPU/GPU power limits and fan curves (`GodModeController`) |
| **ITS** | Intelligent Thermal Solution: Lenovo's power profiles on some IdeaPad/Yoga-class models (`ITSModeFeature`) |
| **Hybrid mode / dGPU / iGPU** | Whether the discrete NVIDIA/AMD GPU or the integrated GPU drives the screen |
| **Spectrum** | Lenovo's per-key RGB keyboards; **RGB keyboard** = 4-zone ones; **LampArray** = Windows Dynamic Lighting |
| **Vantage, Legion Zone, Legion Space** | Lenovo's own apps; LLT can disable them to avoid conflicts (`Lib/SoftwareDisabler/`) |
| **OSD** | On-screen display: the overlay showing FPS, temperatures and mode changes |
| **uiAccess** | A manifest flag that lets the app draw over other windows (OSD); Windows requires a signed app in Program Files for it |
| **Capability ID** | A number identifying a feature in `LENOVO_OTHER_METHOD` (`CapabilityID` enum) |
| **Feature** | In this code: a class implementing `IFeature<T>`, one per setting |
| **Listener** | A class that turns outside events (WMI, Windows) into C# events |
| **Pipeline / trigger / step** | An automation; its "when"; its "do" |
| **Station** | The plug-in system |

## 8. Where to go next

- [ARCHITECTURE.md](ARCHITECTURE.md): the reference map, with the "where to change what" table at the end.
- [USER-GUIDE.md](USER-GUIDE.md): what the app does from a user's point of view.
- [SKINS.md](SKINS.md): the style and animation system added by this mod.
- [SECURITY.md](SECURITY.md), [BUG-FIXES.md](BUG-FIXES.md): what this mod changed and why; good examples of reading code critically.
- [controls.md](controls.md), [localization.md](localization.md): upstream notes on UI controls and translations.
- Microsoft's own tutorials: [C#](https://learn.microsoft.com/dotnet/csharp/), [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/), [async/await](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/), [WMI](https://learn.microsoft.com/windows/win32/wmisdk/wmi-start-page); [Autofac docs](https://autofac.readthedocs.io/).
