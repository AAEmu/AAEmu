using System.Reflection;

namespace AAEmu.Commons.Utils
{
#pragma warning disable CA1000 // Do not declare static members on generic types

    /// <summary>
    /// Base class used for singletons
    /// </summary>
    /// <typeparam name="T">The class type</typeparam>
    public abstract class Singleton<T> where T : class
    {
        private static T _instance;

        /// <summary>
        /// Gets the instance of the singleton
        /// </summary>
        public static T Instance
        {
            get
            {
                OnInit();
                return _instance;
            }
        }

        private static void OnInit()
        {
            if (_instance != null)
                return;
            lock (typeof(T))
            {
                _instance = typeof(T).InvokeMember(typeof(T).Name,
                    BindingFlags.CreateInstance |
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic,
                    null, null, null) as T;
            }
        }
    }
}
