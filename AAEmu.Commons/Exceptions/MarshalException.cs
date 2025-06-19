using System.Diagnostics.CodeAnalysis;

namespace AAEmu.Commons.Exceptions;

/// <summary>
/// 表示在数据编组或解组过程中发生的特定错误。
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MarshalException : GameException // 接下来：这有必要吗？
{
    /// <summary>
    /// 初始化 <see cref="MarshalException"/> 类的新实例，并使用预定义的错误消息。
    /// </summary>
    public MarshalException() : base("Marshal exception")
    {
    }
}
