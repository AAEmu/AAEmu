using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Models.Tasks.Specialty;

public sealed class SpecialtyEventExpiryTask : Task
{
    private readonly SpecialtyManager _specialtyManager;
    private readonly uint _eventId;
    private readonly long _activationToken;
    private readonly SpecialtyEventActivationSource _source;

    public SpecialtyEventExpiryTask(
        SpecialtyManager specialtyManager,
        uint eventId,
        long activationToken)
        : this(specialtyManager, eventId, activationToken, SpecialtyEventActivationSource.Manual)
    {
    }

    internal SpecialtyEventExpiryTask(
        SpecialtyManager specialtyManager,
        uint eventId,
        long activationToken,
        SpecialtyEventActivationSource source)
    {
        _specialtyManager = specialtyManager;
        _eventId = eventId;
        _activationToken = activationToken;
        _source = source;
    }

    public override void Execute()
    {
        _specialtyManager.ExpireSpecialtyEvent(_eventId, _activationToken, _source);
    }
}
