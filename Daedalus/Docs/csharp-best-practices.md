---
name: csharp-best-practices
description: C# language best practices — methods, properties, null handling, async/await, types, and naming conventions for Dalamud plugin development.
sources: [chat]
---

# C# Best Practices

## Methods — Signatures and Conventions

Name async methods with the `Async` suffix:
```csharp
public async Task<bool> LoadDataAsync() { ... }    // Correct
public async Task<bool> LoadData() { ... }          // Wrong — missing Async suffix
```

Return `Task` or `Task<T>` from async methods, never `async void` except for event handlers:
```csharp
// Wrong — exceptions silently swallowed, can't be awaited
private async void DoWork() { ... }

// Correct
private async Task DoWork() { ... }

// Only exception — event handlers
private async void OnButtonClick(object sender, EventArgs e) { ... }
```

## Null Handling — Be Explicit

Use nullable reference types (`?`) to document whether null is expected:
```csharp
public string GetName() { ... }       // Never returns null
public string? GetName() { ... }      // May return null — caller must check
```

Null-check before use — prefer early return (guard clause):
```csharp
public void Process(Player? player)
{
    if (player == null) return;  // Guard clause — bail early
    // Safe to use player below
    player.DoSomething();
}
```

Use null-conditional operator for safe chained access:
```csharp
var name = ClientState.LocalPlayer?.Name?.TextValue;  // null if any link is null
```

Use null-coalescing for defaults:
```csharp
var name = GetPlayerName() ?? "Unknown";
var count = GetCount() ?? 0;
```

Never use `!` (null-forgiving) to suppress warnings unless you are certain the value cannot be null and have a comment explaining why.

## Properties vs Fields

Use properties (not public fields) for anything exposed outside the class:
```csharp
// Wrong
public int JobId;

// Correct
public int JobId { get; private set; }
public bool IsActive { get; set; }
```

Use `readonly` fields for values set only in the constructor:
```csharp
private readonly IPluginLog _log;
public MyClass(IPluginLog log)
{
    _log = log;
}
```

## Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Class | PascalCase | `RotationManager` |
| Method | PascalCase | `GetCurrentJob()` |
| Property | PascalCase | `IsConnected` |
| Private field | _camelCase | `_pluginConfig` |
| Local variable | camelCase | `jobId`, `currentHp` |
| Constant | PascalCase or ALL_CAPS | `MaxBuffSlots` |
| Interface | IPascalCase | `IRotationProvider` |
| Async method | PascalCaseAsync | `LoadDataAsync()` |

## async/await — Critical Rules

Never block on async code — this causes deadlocks:
```csharp
// Wrong — deadlocks in UI/game thread context
var result = GetDataAsync().Result;
var result = GetDataAsync().Wait();

// Correct — await all the way up
var result = await GetDataAsync();
```

Always propagate async up the call stack:
```csharp
// Wrong — blocks the thread
public void DoWork()
{
    var data = GetDataAsync().Result; // Deadlock risk
}

// Correct
public async Task DoWork()
{
    var data = await GetDataAsync();
}
```

Use `ConfigureAwait(false)` in library/plugin code (not UI callbacks):
```csharp
var data = await GetDataAsync().ConfigureAwait(false);
```

## IDisposable — Always Clean Up

Any class that holds hooks, subscriptions, or unmanaged resources must implement `IDisposable`:

```csharp
public sealed class MyService : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up hooks, events, commands here
    }
}
```

In Dalamud plugins — the main plugin class `Dispose()` is called when the plugin is unloaded. Always remove:
- Event subscriptions (`-=`)
- Hook instances (`.Dispose()`)
- Command handlers (`RemoveHandler`)
- IPC providers/subscribers

## Type Selection

| Need | Use |
|---|---|
| True/false | `bool` |
| Whole number, general | `int` |
| Large whole number | `long` |
| Game IDs, entity IDs | `uint` |
| Byte value | `byte` |
| Text | `string` |
| Nullable value type | `int?`, `bool?`, etc. |
| Collection, fixed | `T[]` |
| Collection, variable | `List<T>` |
| Key/value lookup | `Dictionary<TKey, TValue>` |

## String Handling

Prefer string interpolation over concatenation:
```csharp
var msg = $"Player {name} cast {skillName}";  // Correct
var msg = "Player " + name + " cast " + skillName;  // Avoid
```

Use `string.IsNullOrEmpty()` or `string.IsNullOrWhiteSpace()` for empty checks:
```csharp
if (string.IsNullOrEmpty(name)) return;
```

## LINQ — Use Carefully in Game Loop

LINQ allocates — avoid it in hot paths (per-frame updates, high-frequency callbacks):
```csharp
// Fine for one-time setup or infrequent operations
var activeBuffs = buffs.Where(b => b.IsActive).ToList();

// Avoid in per-frame Update() or high-frequency hooks — pre-compute instead
```

## Logging

Use `IPluginLog` (injected service) not `Console.WriteLine` or `Debug.Print`:
```csharp
Log.Debug("Processing job {JobId}", jobId);
Log.Warning("Player pointer was null");
Log.Error(ex, "Failed to load configuration");
```

Use structured logging with parameters, not string interpolation in log calls — lets log systems filter and index properly.
