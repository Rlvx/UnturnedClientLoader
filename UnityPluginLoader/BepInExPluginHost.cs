using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using SDG.Unturned;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPluginLoader
{
    [BepInPlugin("com.rlvx.outbreak.clientloader", "UnityPluginLoader", "1.0.0")]
    public class BepInExPluginHost : BaseUnityPlugin
    {
        

        internal static ClientEventProcessor Instance { get; private set; }
        internal static ManualLogSource PublicLogger { get; private set; }

        private void Awake()
        {
            CrashTrace.Log("BepInExPluginHost", "Awake start");
            PublicLogger = Logger;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                Patches.PatchAll();
            }
            catch (Exception ex)
            {
                CrashTrace.LogException("BepInExPluginHost", ex);
                throw;
            }

            PublicLogger.LogInfo("OutbreakClientLoader ready.");
            PublicLogger.LogInfo("Commands: /ocl load all | /ocl unload all | /ocl load <file>");
            PublicLogger.LogInfo("Diagnostics file: " + CrashTrace.PathForDebug);
            CrashTrace.Log("OutbreakClientLoaderPlugin", "Awake done, diagnostics path: " + CrashTrace.PathForDebug);
            

            // Attendre que la scène soit chargée avant de créer le handler
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            CrashTrace.Log("BepInExPluginHost", "OnDestroy");
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Exception ex = args.ExceptionObject as Exception;
            CrashTrace.Log("BepInExPluginHost", "Unhandled exception (IsTerminating=" + args.IsTerminating + ")");
            CrashTrace.Log("BepInExPluginHost", ex?.ToString() ?? args.ExceptionObject?.ToString() ?? "unknown");
            PublicLogger?.LogError("Unhandled exception (IsTerminating=" + args.IsTerminating + "): " + (ex?.ToString() ?? args.ExceptionObject?.ToString() ?? "unknown"));
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            CrashTrace.Log("BepInExPluginHost", "Unobserved task exception: " + args.Exception);
            PublicLogger?.LogError("Unobserved task exception: " + args.Exception);
            args.SetObserved();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            CrashTrace.Log("BepInExPluginHost", "OnSceneLoaded: " + scene.name + " (" + mode + ")");
            
            // Créer un GameObject avec le composant pour l'Update loop
            GameObject updateObject = new GameObject("ClientEventProcessor");
            ClientEventProcessor processor = updateObject.AddComponent<ClientEventProcessor>();

            processor._loader = new ClientPluginLoader();
            processor._pluginsDirectory = Path.Combine(Paths.PluginPath, "OutbreakClientPlugins");
            Directory.CreateDirectory(processor._pluginsDirectory);

            PublicLogger.LogInfo("Plugins folder: " + processor._pluginsDirectory);
            Instance = processor;
            DontDestroyOnLoad(updateObject);
            PublicLogger.LogInfo("[UnityPluginLoader] Update handler initialized");
            CrashTrace.Log("BepInExPluginHost", "Update handler initialized, plugins dir: " + processor._pluginsDirectory);
        }

        
    }

    public class ClientEventProcessor : MonoBehaviour
    {
        internal ClientPluginLoader _loader;
        internal string _pluginsDirectory;

        private void Update()
        {
            if (_loader == null)
            {
                return;
            }

            try
            {
                _loader.TickAll(Notify);
            }
            catch (Exception ex)
            {
                CrashTrace.LogException("ClientEventProcessor.Update", ex);
                Notify("Update tick failed: " + ex.Message);
            }
        }

        internal bool TryHandleChatCommand(string text)
        {
            CrashTrace.Log("ClientEventProcessor", "TryHandleChatCommand input: " + text);
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("/ocl", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string commandBody = text.Length > 4 ? text.Substring(4).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(commandBody))
            {
                Notify("Usage: /ocl load all | /ocl unload all | /ocl load <file>");
                return true;
            }

            List<string> args = ParseArguments(commandBody);
            if (args.Count == 0)
            {
                Notify("Usage: /ocl load all | /ocl unload all | /ocl load <file>");
                return true;
            }

            string action = args[0].ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "load":
                        HandleLoad(args);
                        return true;
                    case "unload":
                        HandleUnload(args);
                        return true;
                    default:
                        Notify("Unknown action: " + action);
                        Notify("Usage: /ocl load all | /ocl unload all | /ocl load <file>");
                        return true;
                }
            }
            catch (Exception ex)
            {
                CrashTrace.LogException("ClientEventProcessor", ex);
                Notify("Command failed: " + ex);
                return true;
            }
        }

        private void HandleLoad(IReadOnlyList<string> args)
        {
            CrashTrace.Log("ClientEventProcessor", "HandleLoad args: " + string.Join(" | ", args));
            if (args.Count < 2)
            {
                Notify("Usage: /ocl load all | /ocl load <file>");
                return;
            }

            if (string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
            {
                int loadedCount = _loader.LoadAllFromDirectory(_pluginsDirectory, Notify);
                Notify("Loaded plugins: " + loadedCount);
                CrashTrace.Log("ClientEventProcessor", "HandleLoad all completed. Loaded=" + loadedCount);
                return;
            }

            string pluginPath = ResolvePluginPath(args[1]);
            CrashTrace.Log("ClientEventProcessor", "HandleLoad resolved path: " + pluginPath);
            string pluginId = _loader.LoadPlugin(pluginPath, null, Notify);
            Notify("Loaded plugin id: " + pluginId);
            CrashTrace.Log("ClientEventProcessor", "HandleLoad loaded plugin id: " + pluginId);
        }

        private void HandleUnload(IReadOnlyList<string> args)
        {
            if (args.Count >= 2 && string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
            {
                _loader.UnloadAll(Notify);
                Notify("All plugins unloaded.");
                return;
            }

            Notify("Usage: /ocl unload all");
        }

        private string ResolvePluginPath(string rawPath)
        {
            string candidate = rawPath;
            if (!Path.IsPathRooted(candidate))
            {
                candidate = Path.Combine(_pluginsDirectory, candidate);
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (!candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string withExtension = candidate + ".dll";
                if (File.Exists(withExtension))
                {
                    return withExtension;
                }
            }

            throw new FileNotFoundException("Plugin file not found: " + rawPath);
        }

        public static void Notify(string message)
        {
            CrashTrace.Log("Notify", message);
            try
            {
                BepInExPluginHost.PublicLogger?.LogInfo(message);
            }
            catch (Exception ex)
            {
                CrashTrace.LogException("Notify.Logger", ex);
            }

            // During diagnostics, avoid CommandWindow logging because it may re-enter chat internals.
            // CrashTrace + BepInEx logger remain active and are safer here.
        }

        private static List<string> ParseArguments(string text)
        {
            List<string> args = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return args;
            }

            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(text[i]))
                {
                    if (i > start)
                    {
                        args.Add(TrimQuotes(text.Substring(start, i - start)));
                    }

                    while (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
                    {
                        i++;
                    }

                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                args.Add(TrimQuotes(text.Substring(start)));
            }

            return args;
        }

        private static string TrimQuotes(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}