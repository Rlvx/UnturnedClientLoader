using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UnityPluginLoader;

public sealed class ClientPluginLoader
{
    private readonly Dictionary<string, LoadedPlugin> _plugins = new Dictionary<string, LoadedPlugin>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    public IReadOnlyCollection<string> LoadedPluginIds
    {
        get
        {
            lock (_sync)
            {
                return _plugins.Keys.ToArray();
            }
        }
    }

    public string LoadPlugin(
        string pluginAssemblyPath,
        string entryTypeFullName = null,
        Action<string> logger = null)
    {
        CrashTrace.Log("ClientPluginLoader", "LoadPlugin start path=" + pluginAssemblyPath + " entryType=" + (entryTypeFullName ?? "<auto>"));
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            throw new ArgumentException("Plugin path is required.", nameof(pluginAssemblyPath));
        }

        string fullPath = Path.GetFullPath(pluginAssemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Plugin assembly was not found.", fullPath);
        }

        string pluginId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "OutbreakClientLoader", pluginId);
        string copiedAssemblyPath = CopyPluginForShadowLoading(fullPath, cacheDirectory);
        CrashTrace.Log("ClientPluginLoader", "Plugin copied to cache: " + copiedAssemblyPath);

        // Load directly in the main domain so all game statics (ChatManager, Input, etc.) are live.
        PluginDomainBridge bridge = new PluginDomainBridge();
        CrashTrace.Log("ClientPluginLoader", "Bridge created in main domain for pluginId=" + pluginId);

        string sourceDirectory = Path.GetDirectoryName(fullPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        CrashTrace.Log("ClientPluginLoader", "Calling bridge.Load for pluginId=" + pluginId);
        bridge.Load(copiedAssemblyPath, sourceDirectory, entryTypeFullName, null);
        CrashTrace.Log("ClientPluginLoader", "bridge.Load completed for pluginId=" + pluginId + " displayName=" + bridge.DisplayName);

        LoadedPlugin loaded = new LoadedPlugin(
            pluginId,
            fullPath,
            cacheDirectory,
            bridge);

        lock (_sync)
        {
            _plugins.Add(loaded.PluginId, loaded);
        }

        logger?.Invoke(string.Format(
            CultureInfo.InvariantCulture,
            "Loaded plugin '{0}' ({1}) from '{2}'.",
            loaded.DisplayName,
            loaded.PluginId,
            fullPath));
        CrashTrace.Log("ClientPluginLoader", "LoadPlugin success pluginId=" + loaded.PluginId + " displayName=" + loaded.DisplayName);

        return loaded.PluginId;
    }

    public bool UnloadPlugin(string pluginId, Action<string> logger = null)
    {
        LoadedPlugin loaded;
        lock (_sync)
        {
            if (!_plugins.TryGetValue(pluginId, out loaded!))
            {
                return false;
            }

            _plugins.Remove(pluginId);
        }

        return loaded.Unload(logger);
    }

    public string ReloadPlugin(string pluginId, Action<string> logger = null)
    {
        LoadedPlugin loaded;
        lock (_sync)
        {
            if (!_plugins.TryGetValue(pluginId, out loaded!))
            {
                throw new KeyNotFoundException($"Plugin id '{pluginId}' is not loaded.");
            }
        }

        string assemblyPath = loaded.AssemblyPath;
        string entryType = loaded.EntryTypeFullName;

        UnloadPlugin(pluginId, logger);
        return LoadPlugin(assemblyPath, entryType, logger);
    }

    public int LoadAllFromDirectory(string directoryPath, Action<string> logger = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path is required.", nameof(directoryPath));
        }

        string fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException("Plugin directory was not found: " + fullDirectoryPath);
        }

        string[] files = Directory
            .GetFiles(fullDirectoryPath, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int loadedCount = 0;
        foreach (string file in files)
        {
            try
            {
                LoadPlugin(file, null, logger);
                loadedCount++;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Failed to load '" + file + "': " + ex);
            }
        }

        return loadedCount;
    }

    public void UnloadAll(Action<string> logger = null)
    {
        string[] pluginIds;
        lock (_sync)
        {
            pluginIds = _plugins.Keys.ToArray();
        }

        foreach (string pluginId in pluginIds)
        {
            UnloadPlugin(pluginId, logger);
        }
    }

    public void TickAll(Action<string> logger = null)
    {
        CrashTrace.Log("ClientPluginLoader", "TickAll start, plugin count=" + _plugins.Count);
        LoadedPlugin[] loadedPlugins;
        lock (_sync)
        {
            loadedPlugins = _plugins.Values.ToArray();
        }

        if (loadedPlugins.Length == 0)
        {
            return;
        }

        foreach (LoadedPlugin loaded in loadedPlugins)
        {
            CrashTrace.Log("ClientPluginLoader", "Ticking plugin " + loaded.DisplayName);
            loaded.Tick(logger);
        }
    }

    private static string CopyPluginForShadowLoading(string sourceAssemblyPath, string destinationDirectory)
    {
        CrashTrace.Log("ClientPluginLoader", "CopyPluginForShadowLoading source=" + sourceAssemblyPath + " dest=" + destinationDirectory);
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, true);
        }

        Directory.CreateDirectory(destinationDirectory);

        string sourceDirectory = Path.GetDirectoryName(sourceAssemblyPath) ?? throw new InvalidOperationException("Unable to determine plugin directory.");
        foreach (string filePath in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(filePath);
            string destinationFilePath = Path.Combine(destinationDirectory, fileName);
            File.Copy(filePath, destinationFilePath, true);
        }

        // Also copy known dependencies from Librairies folder for AppDomain isolation
        string librariesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Librairies");
        if (Directory.Exists(librariesPath))
        {
            foreach (string knownDep in new[] { "UnityEngine.dll", "UnityEngine.CoreModule.dll", "Assembly-CSharp.dll", "netstandard.dll" })
            {
                string libPath = Path.Combine(librariesPath, knownDep);
                if (File.Exists(libPath))
                {
                    string destPath = Path.Combine(destinationDirectory, knownDep);
                    if (!File.Exists(destPath))
                    {
                        File.Copy(libPath, destPath, false);
                        CrashTrace.Log("ClientPluginLoader", "Copied dependency: " + knownDep);
                    }
                }
            }
        }

        return Path.Combine(destinationDirectory, Path.GetFileName(sourceAssemblyPath));
    }

    private sealed class LoadedPlugin
    {
        public LoadedPlugin(
            string pluginId,
            string assemblyPath,
            string cacheDirectory,
            PluginDomainBridge bridge)
        {
            PluginId = pluginId;
            AssemblyPath = assemblyPath;
            CacheDirectory = cacheDirectory;
            Bridge = bridge;
            EntryTypeFullName = bridge.EntryTypeFullName;
            DisplayName = bridge.DisplayName;
        }

        public string PluginId { get; }
        public string AssemblyPath { get; }
        public string EntryTypeFullName { get; }
        public string DisplayName { get; }
        private string CacheDirectory { get; }
        private PluginDomainBridge Bridge { get; }

        public bool Unload(Action<string> logger)
        {
            try
            {
                Bridge.Unload();
            }
            catch (Exception ex)
            {
                logger?.Invoke("Plugin unload callback failed for " + PluginId + ": " + ex);
            }

            try
            {
                TryDeleteDirectory(CacheDirectory);
                logger?.Invoke("Unloaded plugin (" + PluginId + ").");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Invoke("Failed to unload plugin (" + PluginId + "): " + ex);
                return false;
            }
        }

        public void Tick(Action<string> logger)
        {
            try
            {
                Bridge.Tick();
            }
            catch (Exception ex)
            {
                logger?.Invoke("Tick failed for plugin (" + PluginId + "): " + ex);
            }
        }
    }

    public sealed class CrossDomainLogSink : MarshalByRefObject
    {
        private readonly Action<string> _logger;
        private readonly bool _forwardToCallback;

        public CrossDomainLogSink(Action<string> logger, bool forwardToCallback = false)
        {
            _logger = logger;
            _forwardToCallback = forwardToCallback;
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }

        public void Log(string message)
        {
            CrashTrace.Log("CrossDomainLogSink", message);

            if (!_forwardToCallback)
            {
                return;
            }

            try
            {
                _logger?.Invoke(message);
            }
            catch (Exception ex)
            {
                CrashTrace.LogException("CrossDomainLogSink", ex);
            }
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}
