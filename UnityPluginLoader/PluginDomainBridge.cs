using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UnityPluginLoader;

public sealed class PluginDomainBridge : MarshalByRefObject
{
    private IClientPlugin _plugin;
    private IClientTickable _tickable;
    private ClientPluginLoader.CrossDomainLogSink _logSink;
    private string _pluginDirectory;
    private string _assemblyDirectory;

    public string DisplayName { get; private set; }
    public string EntryTypeFullName { get; private set; }

    public override object InitializeLifetimeService()
    {
        return null;
    }

    public void Load(
        string pluginAssemblyPath,
        string pluginDirectory,
        string entryTypeFullName,
        ClientPluginLoader.CrossDomainLogSink logSink)
    {
        CrashTrace.Log("PluginDomainBridge", "Load start assembly=" + pluginAssemblyPath + " entry=" + (entryTypeFullName ?? "<auto>"));
        if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
        {
            throw new ArgumentException("Plugin assembly path is required.", nameof(pluginAssemblyPath));
        }

        try
        {
            _pluginDirectory = pluginDirectory;
            _logSink = logSink;
            _assemblyDirectory = Path.GetDirectoryName(pluginAssemblyPath) ?? string.Empty;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            byte[] assemblyBytes = File.ReadAllBytes(pluginAssemblyPath);
            CrashTrace.Log("PluginDomainBridge", "Assembly bytes read: " + assemblyBytes.Length);
            Assembly pluginAssembly = Assembly.Load(assemblyBytes);
            CrashTrace.Log("PluginDomainBridge", "Assembly loaded: " + pluginAssembly.FullName);

            Type pluginType = ResolvePluginType(pluginAssembly, entryTypeFullName);
            CrashTrace.Log("PluginDomainBridge", "Resolved plugin type: " + pluginType.FullName);
            CrashTrace.Log("PluginDomainBridge", "Before Activator.CreateInstance");
            object instance = Activator.CreateInstance(pluginType);
            CrashTrace.Log("PluginDomainBridge", "After Activator.CreateInstance");
            IClientPlugin plugin = instance as IClientPlugin;
            if (plugin == null)
            {
                throw new InvalidOperationException("Type '" + pluginType.FullName + "' is not a valid " + nameof(IClientPlugin) + " instance.");
            }
            CrashTrace.Log("PluginDomainBridge", "After IClientPlugin cast");

            _plugin = plugin;
            _tickable = plugin as IClientTickable;
            EntryTypeFullName = pluginType.FullName ?? pluginType.Name;
            CrashTrace.Log("PluginDomainBridge", "Before reading plugin.Name");
            DisplayName = plugin.Name;
            CrashTrace.Log("PluginDomainBridge", "After reading plugin.Name: " + DisplayName);
            CrashTrace.Log("PluginDomainBridge", "Before plugin.OnLoad");
            CrashTrace.Log("PluginDomainBridge", "Calling plugin.OnLoad for " + EntryTypeFullName);
            plugin.OnLoad(new PluginContext(_pluginDirectory, _logSink));
            CrashTrace.Log("PluginDomainBridge", "plugin.OnLoad completed: " + DisplayName);
        }
        catch (Exception ex)
        {
            CrashTrace.LogException("PluginDomainBridge", ex);
            throw;
        }
    }

    public void Tick()
    {
        if (_tickable == null)
        {
            return;
        }

        try
        {
            _tickable.OnUpdate();
        }
        catch (Exception ex)
        {
            CrashTrace.LogException("PluginDomainBridge.Tick", ex);
            _tickable = null; // stop ticking to avoid repeated exceptions, but keep the plugin loaded for diagnostics
        }
    }

    public void Unload()
    {
        if (_plugin == null)
        {
            return;
        }

        try
        {
            _plugin.OnUnload();
            IDisposable disposable = _plugin as IDisposable;
            disposable?.Dispose();
        }
        finally
        {
            _tickable = null;
            _plugin = null;
            _logSink = null;
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
        }
    }

    private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        AssemblyName requested = new AssemblyName(args.Name);
        string fileName = requested.Name + ".dll";

        // Priority: return already-loaded assembly from the current domain.
        // This ensures game assemblies (UnityEngine, Assembly-CSharp, SDG.*) use the live
        // instances from the main runtime instead of a fresh bytes-loaded copy.
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        string candidateInAssemblyDir = Path.Combine(_assemblyDirectory, fileName);
        if (File.Exists(candidateInAssemblyDir))
        {
            CrashTrace.Log("PluginDomainBridge", "AssemblyResolve from assembly dir: " + fileName);
            return Assembly.Load(File.ReadAllBytes(candidateInAssemblyDir));
        }

        string candidateInPluginDir = Path.Combine(_pluginDirectory ?? string.Empty, fileName);
        if (File.Exists(candidateInPluginDir))
        {
            CrashTrace.Log("PluginDomainBridge", "AssemblyResolve from plugin dir: " + fileName);
            return Assembly.Load(File.ReadAllBytes(candidateInPluginDir));
        }

        CrashTrace.Log("PluginDomainBridge", "AssemblyResolve miss: " + fileName);
        return null;
    }

    private static Type ResolvePluginType(Assembly pluginAssembly, string entryTypeFullName)
    {
        if (!string.IsNullOrWhiteSpace(entryTypeFullName))
        {
            Type explicitType = pluginAssembly.GetType(entryTypeFullName, throwOnError: false, ignoreCase: false);
            if (explicitType == null)
            {
                throw new InvalidOperationException("Entry type '" + entryTypeFullName + "' was not found in '" + pluginAssembly.FullName + "'.");
            }

            if (!typeof(IClientPlugin).IsAssignableFrom(explicitType) || explicitType.IsAbstract)
            {
                throw new InvalidOperationException("Entry type '" + entryTypeFullName + "' must be a non-abstract " + nameof(IClientPlugin) + ".");
            }

            return explicitType;
        }

        Type discovered = pluginAssembly
            .GetTypes()
            .FirstOrDefault(type => typeof(IClientPlugin).IsAssignableFrom(type) && !type.IsAbstract);

        if (discovered == null)
        {
            throw new InvalidOperationException("No non-abstract " + nameof(IClientPlugin) + " implementation was found in '" + pluginAssembly.FullName + "'.");
        }

        return discovered;
    }

    private sealed class PluginContext : MarshalByRefObject, IPluginContext
    {
        private readonly ClientPluginLoader.CrossDomainLogSink _logSink;

        public PluginContext(string pluginDirectory, ClientPluginLoader.CrossDomainLogSink logSink)
        {
            PluginDirectory = pluginDirectory;
            _logSink = logSink;
        }

        public string PluginDirectory { get; private set; }

        public override object InitializeLifetimeService()
        {
            return null;
        }

        public void Log(string message)
        {
            CrashTrace.Log("PluginContext", "[Plugin] " + message);
            Trace.WriteLine("[OutbreakClientLoader] " + message);
        }
    }
}
