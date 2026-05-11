namespace UnityPluginLoader;

public interface IClientPlugin
{
    string Name { get; }
    void OnLoad(IPluginContext context);
    void OnUnload();
}

public interface IClientTickable
{
    void OnUpdate();
}

public interface IPluginContext
{
    string PluginDirectory { get; }
    void Log(string message);
}
