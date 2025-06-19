using System.Reflection;

namespace AAEmu.Commons.Utils;

#pragma warning disable CA1000 // Do not declare static members on generic types

/// <summary>
/// 用于单例的基类
/// </summary>
/// <typeparam name="T">类类型</typeparam>
public abstract class Singleton<T> where T : class
{
    private static T _instance; // 存储单例模式的唯一实例。

    /// <summary>
    /// 获取单例的实例
    /// </summary>
    public static T Instance
    {
        get
        {
            OnInit();
            return _instance;
        }
    }

    /// <summary>
    /// 初始化单例实例（如果尚未初始化）。
    /// 此方法采用线程安全的方式（使用 lock）进行延迟初始化。
    /// 它通过反射创建实例，允许非公共构造函数。
    /// </summary>
    private static void OnInit()
    {
        if (_instance != null) // 双重检查锁定模式的第一道检查，减少锁的竞争。
            return;
        lock (typeof(T)) // 确保在多线程环境下只有一个线程可以初始化实例。
        {
            if (_instance == null) // 第二道检查，确保在获取锁后实例仍未被创建。
            {
                // 使用反射创建类型 T 的实例。
                // 这允许单例类拥有私有或受保护的构造函数，这是单例模式的常见做法。
                // typeof(T).Name 假定类名与构造函数名相同（对于构造函数这是正确的，但这里更像是获取类型的默认构造函数）。
                _instance = typeof(T).InvokeMember(typeof(T).Name, // memberName 参数在这里实际上并不用于查找构造函数，因为 CreateInstance 标志已指定。
                    BindingFlags.CreateInstance | // 指定要调用构造函数。
                    BindingFlags.Instance |       // 指定实例成员。
                    BindingFlags.Public |         // 允许匹配公共成员。
                    BindingFlags.NonPublic,       // 允许匹配非公共成员（例如私有构造函数）。
                    null, // Binder: 使用默认绑定器。
                    null, // Target: 对于静态成员或构造函数，此参数被忽略。
                    null  // Args: 没有构造函数参数。
                    ) as T;
            }
        }
    }
}
