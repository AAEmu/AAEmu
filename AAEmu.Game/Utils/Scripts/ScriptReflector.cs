using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AAEmu.Game.Models.Game;
using Microsoft.CodeAnalysis;
using NLog;

namespace AAEmu.Game.Utils.Scripts;

public static class ScriptReflector
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Dictionary<string, ScriptObject> _scriptsObjects = [];

    public static bool Reflect()
    {
        OnLoad();
        return true;
    }

    public static void Clear()
    {
        _scriptsObjects.Clear();
    }

    private static void OnLoad()
    {
        var hasErrors = false;
        _scriptsObjects.Clear();

        // Load all the scripts that implements ICommand interface
        var types = Assembly.GetExecutingAssembly().GetTypes().Where(t
            => t.IsAssignableTo(typeof(ICommand)) && !t.IsInterface && !t.IsAbstract && !t.IsNested && !t.IsAbstract);

        foreach (var type in types)
        {
            try
            {
                var obj = Activator.CreateInstance(type);
                var script = new ScriptObject(type, obj);
                _scriptsObjects.Add(script.Name, script);
                script.Invoke("OnLoad");
            }
            catch (Exception e)
            {
                hasErrors = true;
                Logger.Error($"Error in {type}");
                Logger.Error(e);
            }
        }
        if (hasErrors)
            Logger.Warn($"There were some errors when compiling the user scripts !");
        // throw new Exception("There were errors in the user scripts !");
    }
}
