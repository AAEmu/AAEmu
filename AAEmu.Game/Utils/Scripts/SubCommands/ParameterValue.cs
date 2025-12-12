namespace AAEmu.Game.Utils.Scripts.SubCommands;

public struct ParameterValue(object value)
{
    /// <summary>
    /// Gets the underlying parameter value, for testing purposes.
    /// </summary>
    /// <returns>Underlying parameter value</returns>
    [Obsolete("Prefer usage of implicit operations or As<T>()")]
    public object GetValue()
    {
        return value;
    }

    public T As<T>()
    {
        if (value is T)
            return (T)value;

        throw new InvalidCastException($"Cannot cast {value.GetType().Name} to {typeof(T).Name}");
    }
    public bool Is<T>()
    {
        return value is T;
    }

    public static implicit operator float(ParameterValue value)
    {
        return value.As<float>();
    }

    public static implicit operator double(ParameterValue value)
    {
        return value.As<double>();
    }

    public static implicit operator long(ParameterValue value)
    {
        return value.As<long>();
    }

    public static implicit operator ulong(ParameterValue value)
    {
        return value.As<ulong>();
    }

    public static implicit operator int(ParameterValue value)
    {
        return value.As<int>();
    }

    public static implicit operator uint(ParameterValue value)
    {
        return value.As<uint>();
    }

    public static implicit operator byte(ParameterValue value)
    {
        return value.As<byte>();
    }

    public static implicit operator string(ParameterValue value)
    {
        return value.As<string>();
    }

    public override string ToString()
    {
        return value.ToString();
    }
}
