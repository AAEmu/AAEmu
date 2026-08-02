using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

public readonly record struct AggroComponents(int Damage, int Heal, int Direct)
{
    public int Total => Damage + Heal + Direct;
}

public class Aggro
{
    private readonly object _lock = new();

    public Aggro(Unit owner)
    {
        Owner = owner;
    }

    public Aggro(Unit owner, AggroComponents components)
        : this(owner)
    {
        _damageAggro = components.Damage;
        _healAggro = components.Heal;
        _directAggro = components.Direct;
    }

    public Unit Owner { get; }

    //Considering using interlocked methods instead of a lock, need to research how they work..
    private int _damageAggro;
    public int DamageAggro
    {
        get
        {
            lock (_lock)
            {
                return _damageAggro;
            }
        }
    }

    private int _healAggro;
    public int HealAggro
    {
        get
        {
            lock (_lock)
            {
                return _healAggro;
            }
        }
    }

    private int _directAggro;
    public int DirectAggro
    {
        get
        {
            lock (_lock)
            {
                return _directAggro;
            }
        }
    }

    public int TotalAggro
    {
        get
        {
            lock (_lock)
            {
                return _damageAggro + _healAggro + _directAggro;
            }
        }
    }

    public void AddAggro(AggroKind kind, int amount)
    {
        lock (_lock)
        {
            if (kind == AggroKind.Damage)
                _damageAggro += amount;
            else if (kind == AggroKind.Heal)
                _healAggro += (int)(amount * 0.6f);
            else if (kind == AggroKind.Etc)
                _directAggro += amount;
        }
    }

    public AggroComponents GetComponents()
    {
        lock (_lock)
        {
            return new AggroComponents(_damageAggro, _healAggro, _directAggro);
        }
    }

    public void ApplyComponentSelectors(
        bool selectDamage,
        bool selectHeal,
        bool selectDirect,
        int applyValue)
    {
        lock (_lock)
        {
            if (selectDamage)
                _damageAggro = applyValue;
            if (selectHeal)
                _healAggro = applyValue;
            if (selectDirect)
                _directAggro = applyValue;
        }
    }

    public void RemoveAggro(AggroKind kind, int amount) => AddAggro(kind, -amount);
}
