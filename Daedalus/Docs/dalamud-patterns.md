---
name: dalamud-patterns
description: Dalamud-specific plugin patterns — service injection, FFXIVClientStructs, hooks, IPC, and plugin lifecycle. For Daedalus, Sealbreaker, and other FFXIV plugins.
sources: [chat]
---

# Dalamud Plugin Patterns

## Plugin Structure — The Basics

Every Dalamud plugin has a main plugin class that implements `IDalamudPlugin`. Services are injected via `[PluginService]` attributes — never instantiate Dalamud services manually.

```csharp
public sealed class MyPlugin : IDalamudPlugin
{
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public MyPlugin()
    {
        // Constructor runs after injection — services are available here
    }

    public void Dispose()
    {
        // Always unregister hooks, events, and commands here
    }
}
```

## Service Injection Rules

- Use `[PluginService]` for all Dalamud services — never `new()` them
- Mark injected properties `internal static` so other classes in the plugin can access them
- Initialize with `= null!` to suppress nullable warnings — Dalamud guarantees they're set before the constructor runs
- Always implement `IDisposable` and clean up in `Dispose()`

## FFXIVClientStructs — Accessing Game Memory

FFXIVClientStructs provides C# wrappers for game structs. All interop is `unsafe`:

```csharp
unsafe
{
    var player = (Character*)ClientState.LocalPlayer?.Address;
    if (player == null) return;

    // Access game struct fields directly
    var currentHp = player->Health;
}
```

Rules:
- Always null-check pointers before dereferencing
- Wrap game struct access in `unsafe` blocks
- Never store raw game pointers across frames — re-fetch each use
- Use `FFXIVClientStructs` types, not manually offset pointers

## Hooks — Intercepting Game Functions

Use `IGameInteropProvider` to hook game functions:

```csharp
private Hook<SomeFunctionDelegate>? _someHook;

private delegate void SomeFunctionDelegate(IntPtr a1, uint a2);

public MyPlugin(IGameInteropProvider gameInterop)
{
    _someHook = gameInterop.HookFromSignature<SomeFunctionDelegate>(
        "signature here",
        SomeFunctionDetour
    );
    _someHook.Enable();
}

private void SomeFunctionDetour(IntPtr a1, uint a2)
{
    // Do work, then call original
    _someHook!.Original(a1, a2);
}

public void Dispose()
{
    _someHook?.Dispose(); // Disposes and disables the hook
}
```

- Always call the original function unless you intentionally want to block it
- Always dispose hooks in `Dispose()`
- Never enable a hook you've already disposed

## Commands

Register slash commands via `ICommandManager`:

```csharp
[PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

public MyPlugin()
{
    CommandManager.AddHandler("/mycommand", new CommandInfo(OnCommand)
    {
        HelpMessage = "Description of the command"
    });
}

private void OnCommand(string command, string args) { ... }

public void Dispose()
{
    CommandManager.RemoveHandler("/mycommand");
}
```

Always remove command handlers in `Dispose()`.

## UI / ImGui

Dalamud uses ImGui for plugin UI. Draw UI in the `IUiBuilder.Draw` event:

```csharp
[PluginService] internal static IUiBuilder UiBuilder { get; private set; } = null!;

public MyPlugin()
{
    UiBuilder.Draw += DrawUI;
}

private void DrawUI()
{
    // ImGui calls go here
    if (ImGui.Begin("My Window"))
    {
        ImGui.Text("Hello");
        ImGui.End();
    }
}

public void Dispose()
{
    UiBuilder.Draw -= DrawUI; // Always unsubscribe
}
```

## IPC — Communicating Between Plugins

For Daedalus exposing data to other plugins or consuming from them:

```csharp
// Providing IPC
var provider = pluginInterface.GetIpcProvider<uint, bool>("MyPlugin.IsJobActive");
provider.RegisterFunc((jobId) => IsJobActive(jobId));

// Consuming IPC from another plugin
var subscriber = pluginInterface.GetIpcSubscriber<uint, bool>("OtherPlugin.SomeFunction");
var result = subscriber.InvokeFunc(jobId);
```

Always check if the IPC provider exists before calling — the other plugin may not be loaded:
```csharp
try { result = subscriber.InvokeFunc(arg); }
catch (IpcNotReadyError) { /* other plugin not loaded */ }
```

## Configuration

Save plugin config via `IPluginInterface.SavePluginConfig`:

```csharp
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool SomeSetting { get; set; } = true;
}

// Load
var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

// Save
pluginInterface.SavePluginConfig(config);
```

## Plugin Restrictions — What NOT to Do

Dalamud has strict rules. Never generate code that:
- Automates actions without direct user input (no auto-crafting, auto-rolling loot)
- Interacts with game servers outside normal player actions
- Provides combat advantages (no showing non-telegraphed AOEs, no PvP helpers)
- Parses DPS or logs raid data
- Collects other players' account/character IDs
- Bypasses Mog Station purchases

For combat plugins (like Daedalus rotation helpers): only display information, never automate button presses or submit actions to the server automatically.

## API Version Updates

When FFXIV patches, Dalamud API versions bump. Plugin stops loading until updated:
1. Update `DalamudPackager` version in `.csproj`
2. Update the API level in `{PluginName}.json`
3. Retest all hooks — signatures may have changed
4. Check `#dev` channel on Dalamud Discord for breaking change notices

## PR Lessons Learned

Add entries here as issues are caught in review:
- Always dispose hooks, events, and command handlers in `Dispose()`
- Never store raw game pointers as fields — re-fetch each use
- Null-check all `ClientState` properties before use (player may be null in lobby/loading)
