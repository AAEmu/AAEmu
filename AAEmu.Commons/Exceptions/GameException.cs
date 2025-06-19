using System;
using System.Diagnostics.CodeAnalysis;

namespace AAEmu.Commons.Exceptions;

/// <summary>
/// 游戏中通用的自定义异常基类。
/// </summary>
[ExcludeFromCodeCoverage]
public class GameException : Exception
{
    /// <summary>
    /// 初始化 <see cref="GameException"/> 类的新实例，并使用指定的错误消息。
    /// </summary>
    /// <param name="message">描述错误的错误消息。</param>
    public GameException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="GameException"/> 类的新实例，并使用指定的错误消息和对导致此异常的内部异常的引用。
    /// </summary>
    /// <param name="message">描述错误的错误消息。</param>
    /// <param name="innerException">导致当前异常的异常；如果未指定内部异常，则为 null 引用。</param>
    public GameException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
