# UnityPluginLoader - Test Plugin

This plugin is a sample custom DLL to test `UnityPluginLoader` load and unload capabilities.

## Build output

After build, take files from:

- `Outbreak_ClientLoader_TestPlugin/bin/Release/net46`

Copy at least these files into your Unturned client folder:

- `BepInEx/plugins/OutbreakClientPlugins/OutbreakClientHotTestPlugin.dll`
- `BepInEx/plugins/UnityPluginLoader.dll`

## In-game commands

Use chat:

- `/ocl load OutbreakClientHotTestPlugin.dll`
- `/ocl unload all`

You can also use:

- `/ocl load all`

## Expected behavior

When loaded:

- "Loaded" message appears in BepInEx console
- A pulse log appears every 5 seconds
- Press `F9` to emit a test message via ChatManager.sendChat()

When unloaded:

- "Unloaded cleanly" message appears in logs
- Pulse logs stop
