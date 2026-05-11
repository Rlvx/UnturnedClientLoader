# UnityPluginLoader

A generic BepInEx plugin that enables hot-loading and hot-unloading of custom DLL plugins in-game. Supports direct access to game APIs without AppDomain isolation.

## Runtime

- BepInEx 5 (`net46`)
- Unity-based games (tested with Unturned on Mono)

## In-game commands

Commands are sent through the chat box:

- `/ocl load all` - Load all plugins from the plugins directory
- `/ocl unload all` - Unload all plugins
- `/ocl load <file>` - Load a specific plugin

By default, `<file>` is searched inside:

- `BepInEx/plugins/OutbreakClientPlugins`

## Plugin contract

Custom plugin DLLs loaded by this loader should reference `UnityPluginLoader.dll` and implement:

```csharp
using UnityPluginLoader;

public sealed class MyPlugin : IClientPlugin, IClientTickable
{
    public string Name => "MyPlugin";

    public void OnLoad(IPluginContext context)
    {
        context.Log("MyPlugin loaded");
    }

    public void OnUnload()
    {
        // Cleanup hooks and resources.
    }

    public void OnUpdate()
    {
        // Called every frame. Direct access to game APIs (ChatManager, Input, etc.)
    }
}
```

## Features

- **Direct runtime access**: Plugins run in the main AppDomain with access to live game objects (ChatManager, InputEx, etc.)
- **Hot-reload**: Load/unload plugins without restarting the game or killing the process
- **OnUpdate dispatch**: Frame-by-frame OnUpdate callbacks for plugins implementing `IClientTickable`
- **File-based diagnostics**: CrashTrace logs for debugging when BepInEx logging fails

## Notes

- Plugin DLLs are copied to a temp cache before load to avoid file locking on source files.
- Unload calls `OnUnload()` for cleanup; assembly remains in memory (limitation of .NET Framework without AppDomain unloading).
- Plugins have full access to all game static instances (ChatManager, Input, etc.) for UI, messaging, and game interaction.
