using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Core.Managers.World;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class ButlerManager(IButlerRepository repository) : Singleton<ButlerManager>, IButlerManager, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private readonly Dictionary<uint, CharacterButler> _byCharacter = [];
    private readonly Dictionary<uint, uint> _characterByHouse = [];
    private readonly HashSet<uint> _deletedCharacters = [];

    public ButlerManager() : this(new MySqlButlerRepository()) { }

    public void Load()
    {
        lock (_sync)
        {
            _byCharacter.Clear();
            _characterByHouse.Clear();
            _deletedCharacters.Clear();
            foreach (var record in repository.LoadAll())
            {
                var butler = new CharacterButler(record.CharacterId);
                butler.Apply(record);
                _byCharacter[record.CharacterId] = butler;
                if (record.HouseId != 0)
                    _characterByHouse[record.HouseId] = record.CharacterId;
            }
        }
    }

    public CharacterButler GetOrCreate(uint characterId)
    {
        lock (_sync)
        {
            if (!_byCharacter.TryGetValue(characterId, out var butler))
                _byCharacter[characterId] = butler = new CharacterButler(characterId);
            return butler;
        }
    }

    public ButlerOperationResult Bind(Character character, House house, Func<uint, House> registeredHouseResolver)
    {
        if (character == null || house == null)
            return new ButlerOperationResult(false, ErrorMessageType.InteractionPermissionDeny);

        lock (house.LifecycleSyncRoot)
        {
            if (house.IsRemovedFromWorld || !ReferenceEquals(registeredHouseResolver?.Invoke(house.Id), house) ||
                house.OwnerId != character.Id || house.CurrentStep != -1)
                return new ButlerOperationResult(false, ErrorMessageType.InteractionPermissionDeny);

            lock (_sync)
            {
                if (_deletedCharacters.Contains(character.Id))
                    return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);
                var butler = GetOrCreate(character.Id);
                lock (butler.SyncRoot)
                {
                    if (butler.HouseId != 0 ||
                        _characterByHouse.TryGetValue(house.Id, out var boundCharacterId) &&
                        boundCharacterId != character.Id)
                        return new ButlerOperationResult(false, ErrorMessageType.AlreadyRequested);

                    var record = butler.Snapshot() with { HouseId = house.Id };
                    try
                    {
                        if (!repository.TryChangeHouse(record, 0))
                            return new ButlerOperationResult(false, ErrorMessageType.AlreadyRequested);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to bind farmhand for character {0} to house {1}", character.Id,
                            house.Id);
                        return new ButlerOperationResult(false, ErrorMessageType.InternalError);
                    }

                    butler.Apply(record);
                    _characterByHouse[house.Id] = character.Id;
                    return new ButlerOperationResult(true, ErrorMessageType.NoErrorMessage);
                }
            }
        }
    }

    public ButlerOperationResult Unbind(Character character)
    {
        if (character == null)
            return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);

        lock (_sync)
        {
            if (_deletedCharacters.Contains(character.Id))
                return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);
            var butler = GetOrCreate(character.Id);
            if (butler.HouseId == 0)
                return new ButlerOperationResult(false, ErrorMessageType.NoInteractionAvailable);
            return UnbindLocked(butler);
        }
    }

    public bool UnbindHouse(uint houseId, bool notifyOwner = true)
    {
        if (houseId == 0)
            return true;

        uint characterId;
        ButlerOperationResult result;
        lock (_sync)
        {
            if (!_characterByHouse.TryGetValue(houseId, out characterId))
                return true;
            result = UnbindLocked(_byCharacter[characterId]);
        }

        if (result.Success && notifyOwner && WorldManager.Instance.GetCharacterById(characterId) is { } owner)
            owner.SendPacket(new SCButlerUnboundPacket((ushort)ErrorMessageType.NoErrorMessage));
        return result.Success;
    }

    private ButlerOperationResult UnbindLocked(CharacterButler butler)
    {
        lock (butler.SyncRoot)
        {
            var oldHouseId = butler.HouseId;
            var record = butler.Snapshot() with { HouseId = 0, RemainProductionCost = 0 };
            try
            {
                if (!repository.TryChangeHouse(record, oldHouseId))
                    return new ButlerOperationResult(false, ErrorMessageType.InternalError);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to unbind farmhand for character {0} from house {1}",
                    butler.CharacterId, oldHouseId);
                return new ButlerOperationResult(false, ErrorMessageType.InternalError);
            }

            butler.Apply(record);
            _characterByHouse.Remove(oldHouseId);
            return new ButlerOperationResult(true, ErrorMessageType.NoErrorMessage);
        }
    }

    public void RemoveCharacter(uint characterId)
    {
        lock (_sync)
        {
            _byCharacter.TryGetValue(characterId, out var butler);
            lock (butler?.SyncRoot ?? _sync)
            {
                try
                {
                    repository.Delete(characterId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to delete farmhand state for character {0}", characterId);
                    throw;
                }

                _deletedCharacters.Add(characterId);
                if (butler != null)
                {
                    butler.IsDeleted = true;
                    if (butler.HouseId != 0)
                        _characterByHouse.Remove(butler.HouseId);
                    _byCharacter.Remove(characterId);
                }
            }
        }
    }

    public ButlerPresentation GetPresentation(Character character)
        => GetPresentation(character, HousingManager.Instance.GetHouseById);

    internal ButlerPresentation GetPresentation(Character character, Func<uint, House> houseResolver)
    {
        lock (_sync)
            if (_deletedCharacters.Contains(character.Id))
                return new ButlerPresentation(false, CharacterButler.ResetWire, string.Empty);
        var butler = GetOrCreate(character.Id);
        while (true)
        {
            uint expectedHouseId;
            lock (_sync)
            lock (butler.SyncRoot)
            {
                expectedHouseId = butler.HouseId;
                if (expectedHouseId == 0)
                    return new ButlerPresentation(false, butler.FreeWire, string.Empty);
            }

            var house = houseResolver(expectedHouseId);
            if (house == null)
            {
                if (TryUnbindExpected(butler, expectedHouseId))
                    return GetFreePresentation(butler);
                continue;
            }

            lock (house.LifecycleSyncRoot)
            {
                if (house.IsRemovedFromWorld || house.Id != expectedHouseId || house.OwnerId != character.Id ||
                    house.CurrentStep != -1)
                {
                    if (TryUnbindExpected(butler, expectedHouseId))
                        return GetFreePresentation(butler);
                    continue;
                }

                lock (_sync)
                lock (butler.SyncRoot)
                {
                    if (butler.HouseId != expectedHouseId)
                        continue;
                    return new ButlerPresentation(
                        true,
                        ButlerInfoWire.Empty(
                            character.Id,
                            CharacterBlocked.LocalWorldId,
                            butler.Name,
                            house.TlId,
                            butler.LaborPower,
                            butler.LpChargedAmount,
                            butler.RemainProductionCost),
                        house.Name ?? string.Empty);
                }
            }
        }
    }

    private bool TryUnbindExpected(CharacterButler butler, uint expectedHouseId)
    {
        lock (_sync)
        {
            lock (butler.SyncRoot)
            {
                if (butler.HouseId != expectedHouseId)
                    return false;
                var result = UnbindLocked(butler);
                if (!result.Success)
                    Logger.Warn("Could not clear stale farmhand house {0} for character {1}", expectedHouseId,
                        butler.CharacterId);
                return true;
            }
        }
    }

    private ButlerPresentation GetFreePresentation(CharacterButler butler)
    {
        lock (_sync)
        lock (butler.SyncRoot)
            return new ButlerPresentation(false, butler.FreeWire, string.Empty);
    }

    public bool IsHouseBound(uint houseId)
    {
        lock (_sync)
            return houseId != 0 && _characterByHouse.ContainsKey(houseId);
    }

    public void Save(CharacterButler butler, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (butler == null)
            return;
        // The state lock orders this snapshot against immediate bind/unbind writes. MySQL's row lock then
        // ensures a bind waiting behind a periodic save is the last writer after that save commits.
        lock (butler.SyncRoot)
        {
            if (butler.IsDeleted)
                return;
            repository.Save(butler.Snapshot(), connection, transaction);
        }
    }
}
