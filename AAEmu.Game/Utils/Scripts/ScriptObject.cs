using System;
using System.Reflection;

namespace AAEmu.Game.Utils.Scripts
{
    public class ScriptObject
    {
        private readonly Type _type;
        private readonly object _instance;

        public string Name => _type.Namespace + "." + _type.Name;

        public ScriptObject(Type t, object o)
        {
            _type = t;
            _instance = o;
        }

        public object Invoke(string methodName)
        {
            var method = _type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            return method != null ? method.Invoke(_instance, null) : null;
        }
    }
}