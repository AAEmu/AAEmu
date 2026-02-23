using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace CgfConverter.Utils;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class TaggedLogger
{
    private const char IndentChar = ' ';
    private const int IndentMultiplier = 2;

    private readonly string _logTag;

    public TaggedLogger(string logTag, TaggedLogger? parentLogger = null) =>
        _logTag = logTag + (parentLogger is null ? "" : ":" + parentLogger._logTag);

    public void V(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Verbose, null, format, args);
    public void D(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Debug, null, format, args);
    public void I(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Info, null, format, args);
    public void W(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Warning, null, format, args);
    public void E(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Error, null, format, args);
    public void C(string? format = null, params object?[] args) => DoLog(LogLevelEnum.Critical, null, format, args);

    public void V(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Verbose, e, format, args);

    public void D(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Debug, e, format, args);

    public void I(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Info, e, format, args);

    public void W(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Warning, e, format, args);

    public void E(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Error, e, format, args);

    public void C(Exception e, string? format = null, params object?[] args)
        => DoLog(LogLevelEnum.Critical, e, format, args);

    public void V<T>(string? format = null, params object?[] args)
        => DoLog<T>(LogLevelEnum.Verbose, null, format, args);

    public T D<T>(string? format = null, params object?[] args) => DoLog<T>(LogLevelEnum.Debug, null, format, args);
    public T I<T>(string? format = null, params object?[] args) => DoLog<T>(LogLevelEnum.Info, null, format, args);
    public T W<T>(string? format = null, params object?[] args) => DoLog<T>(LogLevelEnum.Warning, null, format, args);
    public T E<T>(string? format = null, params object?[] args) => DoLog<T>(LogLevelEnum.Error, null, format, args);
    public T C<T>(string? format = null, params object?[] args) => DoLog<T>(LogLevelEnum.Critical, null, format, args);

    public T V<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Verbose, e, format, args);

    public T D<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Debug, e, format, args);

    public T I<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Info, e, format, args);

    public T W<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Warning, e, format, args);

    public T E<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Error, e, format, args);

    public T C<T>(Exception e, string? format = null, params object?[] args) =>
        DoLog<T>(LogLevelEnum.Critical, e, format, args);

    private T DoLog<T>(LogLevelEnum level, Exception? e, string? format, object?[] args)
    {
        DoLog(level, e, format, args);
        return default!;
    }

    private void DoLog(LogLevelEnum level, Exception? e, string? format, object?[] args)
    {
        if (level < Utilities.LogLevel && level < Utilities.DebugLevel)
            return;

        var sb = new StringBuilder();
        sb.Append('[').Append(level switch
        {
            LogLevelEnum.Verbose => "VRB:",
            LogLevelEnum.Debug => "DBG:",
            LogLevelEnum.Info => "INF:",
            LogLevelEnum.Warning => "WRN:",
            LogLevelEnum.Error => "ERR:",
            LogLevelEnum.Critical => "CRI:",
            LogLevelEnum.None => "---:",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        }).Append(_logTag).Append(']');
        if (!string.IsNullOrWhiteSpace(format))
        {
            var formatted = args.Any() ? string.Format(format, args) : format;
            sb.Append(' ').Append(formatted.ReplaceLineEndings("\n" + new string(IndentChar, IndentMultiplier)));
        }
        sb.AppendLine();

        if (e is not null)
            FormatException(sb, e);

        var s = sb.ToString();
        if (level >= Utilities.LogLevel)
            Console.Write(s);
        if (level >= Utilities.DebugLevel)
            Debug.Write(s);
    }

    private static void FormatException(StringBuilder sb, Exception e, int depth = 1)
    {
        sb.Append(IndentChar, depth * IndentMultiplier)
            .Append(e.GetType().Name).Append(": ").Append(e.Message)
            .AppendLine();
        if (e is AggregateException ae)
        {
            foreach (var ie in ae.InnerExceptions)
                FormatException(sb, ie, depth + 1);
        }
        else if (e.StackTrace is { } st)
        {
            sb.Append(IndentChar, depth * IndentMultiplier)
                .Append(st.ReplaceLineEndings("\n" + new string(IndentChar, depth * IndentMultiplier)))
                .AppendLine();
        }
    }
}