using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// 来源：https://www.cyberforum.ru/blogs/529033/blog3833.html
/*
 * 静态类是所有 object 类型对象的扩展，包含一个方法 - CheckInterval。
 * 此方法接收一个以毫秒为单位的时间间隔作为参数，并返回一个 bool 类型的值。
 * 该方法会自动记录其上次触发的时间，如果时间尚未到达，则返回 false，
 * 如果时间间隔已过，则返回 true。当返回 true 时，该方法会记录当前时间，
 * 并且在下次返回 true 之前，至少会等待指定的时间间隔。
 * 此助手类的设计使其能够自动为每个调用 CheckInterval 的对象记录触发时间。
 * 此外，如果您在一个类中有多个 CheckInterval 调用，则会为每个调用创建一个单独的时间计数器。
 * 这是通过使用 [CallerLineNumber] 特性实现的，该特性会将调用 CheckInterval 方法的代码行号传递给该方法。
 *
 *  因此，这样的代码也能正常工作：
 *  void DoSomething()
 *  {
 *      // 操作 1：每秒最多执行一次
 *      if(this.CheckInterval(1000))
 *          DoAction1();      
 *      // 操作 2：每 3 秒最多执行一次
 *      if(this.CheckInterval(3000))
 *          DoAction2();
 *  }
 *
 * 静态类包含一个方法 - Triggered。它接收一个 bool 类型的参数，并且该方法本身也返回 bool 类型的值。
 * Triggered 方法会自动记录其参数的先前值，如果参数从 false 变为 true，则返回 true。
 * 与前一个助手类一样，Triggered 会自动为每个对象以及调用它的每一行代码存储条件的先前值。
 * 因此，我们可以在一个方法中进行多次 Triggered 调用 - 会为每次调用创建一个单独的状态标志：
 *
 * void CheckDistanceAndSayHello()
 *  {
 *      // 计算 NPC 和玩家之间的距离
 *      var dist = CalcDist(player, npc);      
 *      // 如果距离小于 1 米 - 说一次“你好”
 *      if (this.Triggered(dist < 1))
 *          SayHello();      
 *      // 如果距离大于 2 米 - 说一次“再见”
 *      if (this.Triggered(dist > 2))
 *          SayBay();
 *  }
 */

namespace AAEmu.Commons.Utils;

public static class Helper
{
    private static Dictionary<Tuple<object, int>, DateTime> intervals = new();

    /// <summary>
    /// 如果（自上次触发后）经过的时间不少于指定的时间间隔，则返回 true
    /// </summary>
    public static bool CheckInterval(this object caller, int interval = 1000, [CallerLineNumber] int lineNumber = 0)
    {
        // 获取当前时间
        var now = DateTime.UtcNow;

        // 生成由调用对象和调用该方法的代码行号组成的键
        var key = new Tuple<object, int>(caller, lineNumber);

        // 获取此键的下次触发时间
        if (!intervals.TryGetValue(key, out var next))
            next = now;// 如果字典中还没有时间 - 我们认为现在需要触发

        // 时间还没到？
        if (next > now)
            return false;

        // 生成下次触发时间
        intervals[key] = now.AddMilliseconds(interval);

        // 时间到了 - 返回 true
        return true;
    }

    private static ConcurrentDictionary<Tuple<object, int>, bool> conditions = new();

    /// <summary>
    /// 如果条件从 false 变为 true，则返回 true
    /// </summary>
    public static bool Triggered(this object sender, bool condition, [CallerLineNumber] int lineNumber = 0)
    {
        // 生成由调用对象和调用该方法的代码行号组成的键
        var key = new Tuple<object, int>(sender, lineNumber);

        // 获取条件的先前值
        if (!conditions.TryGetValue(key, out var old))
            old = false;

        // 记住新状态
        conditions[key] = condition;

        // 如果当前条件满足，而之前不满足 - 返回 true
        if (condition && !old)
            return true;

        // 否则 - 返回 false
        return false;
    }
}
