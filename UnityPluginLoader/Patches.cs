using System.Collections.Generic;
using HarmonyLib;
using SDG.Unturned;
using UnityEngine;
using System;

namespace UnityPluginLoader
{
    internal static class Patches
    {
        internal static Harmony PatcherInstance;
        [ThreadStatic]
        private static bool _isHandlingChat;

        internal static bool IsHandlingChat => _isHandlingChat;

        internal static void PatchAll()
        {
            PatcherInstance = new Harmony("net.toto.patches");
            PatcherInstance.PatchAll();
            BepInExPluginHost.PublicLogger?.LogInfo("Patches Done !!");
        }
        internal static void UnpatchAll()
        {
            PatcherInstance.UnpatchSelf();
        }
        [HarmonyPatch]
        internal static class InternalPatches
        {

            [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.sendChat))]
            [HarmonyPrefix]
            static bool SendChatMessage(EChatMode mode, string text)
            {
                if (_isHandlingChat)
                {
                    return true;
                }

                ClientEventProcessor plugin = BepInExPluginHost.Instance;
                if (plugin == null)
                {
                    BepInExPluginHost.PublicLogger?.LogWarning("Plugin instance is null.");
                    return true;
                }
                try
                {
                    _isHandlingChat = true;
                    bool handled = plugin.TryHandleChatCommand(text);
                    return !handled;
                }
                catch (Exception ex)
                {
                    BepInExPluginHost.PublicLogger?.LogError("Unhandled exception in chat patch: " + ex);
                    return true;
                }
                finally
                {
                    _isHandlingChat = false;
                }
            }
            
        }
        
    }
}



