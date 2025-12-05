using System.Reflection;

namespace AAEmu.Game.Utils.Scripts;

public class ScriptObject(Type t, object o)
{
    public string Name => t.Namespace + "." + t.Name;

    public object Invoke(string methodName)
    {
        var method = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        return method != null ? method.Invoke(o, null) : null;
    }
}