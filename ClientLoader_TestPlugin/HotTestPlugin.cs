using System;
using UnityPluginLoader;
using SDG.Unturned;
using UnityEngine;

namespace ClientLoader.TestPlugin;

public sealed class TestPlugin : IClientPlugin
    , IClientTickable
{
    private IPluginContext _context;
    private int _pulseCount;
    private float _nextPulseAt;
    private string _logFilePath;

    public string Name => "ClientLoaderTestPlugin";

    public void OnLoad(IPluginContext context)
    {
        _context = context;
        _pulseCount = 0;
        _nextPulseAt = Time.realtimeSinceStartup + 2f;
        _logFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClientLoader", "TestPlugin.log");
        Log("Loaded. Main-thread pulse enabled (2s then every 5s).");
        Log("Log file: " + _logFilePath);
    }

    public void OnUnload()
    {
        Log("Unloaded cleanly.");
    }

    public void OnUpdate()
    {
        if (Time.realtimeSinceStartup >= _nextPulseAt)
        {
            _pulseCount++;
            Log("Pulse #" + _pulseCount);
            _nextPulseAt = Time.realtimeSinceStartup + 5f;
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            try
            {
                ChatManager.sendChat(EChatMode.GLOBAL, "Hello from the test plugin! (F9)");
                Log("F9 pressed, chat message sent.");
            }
            catch (Exception ex)
            {
                Log("F9 pressed, sendChat failed: " + ex);
            }
        }
    }

    private void Log(string message)
    {
        _context?.Log(message);

        try
        {
            string line = DateTime.UtcNow.ToString("O") + " [TestPlugin] " + message + Environment.NewLine;
            System.IO.File.AppendAllText(_logFilePath, line);
        }
        catch
        {
            // Best effort only.
        }
    }
}
