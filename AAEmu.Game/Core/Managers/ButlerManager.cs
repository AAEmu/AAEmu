using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Core.Managers.World;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class ButlerManager : Singleton<ButlerManager>, IButlerManager, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private readonly Dictionary<uint, CharacterButler> _byCharacter = [];
    private readonly Dictionary<uint, uint> _characterByHouse = [];
    private readonly HashSet<uint> _deletedCharacters = [];
    private readonly IButlerRepository repository;
    private readonly IButlerUnbindService _unbindService;
    private readonly IItemManager _itemManager;
    private readonly Action<Character, GamePacket> _publishPacket;
    private readonly Func<uint, Character> _characterResolver;

    public ButlerManager() : this(new MySqlButlerRepository())
    {
    }

    private ButlerManager(MySqlButlerRepository repository) : this(
        repository,
        new ButlerUnbindService(
            repository,
            MailManager.Instance,
            ItemManager.Instance,
            NameManager.Instance),
        ItemManager.Instance)
    {
    }

    public ButlerManager(IButlerRepository repository, ButlerUnbindService unbindService)
        : this(repository, (IButlerUnbindService)unbindService, ItemManager.Instance)
    {
    }

    public ButlerManager(IButlerRepository repository, ButlerUnbindService unbindService, IItemManager itemManager)
        : this(repository, (IButlerUnbindService)unbindService, itemManager)
    {
    }

    internal ButlerManager(
        IButlerRepository repository,
        IButlerUnbindService unbindService,
        IItemManager itemManager = null,
        Action<Character, GamePacket> publishPacket = null,
        Func<uint, Character> characterResolver = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _unbindService = unbindService ?? throw new ArgumentNullException(nameof(unbindService));
        _itemManager = itemManager ?? ItemManager.Instance;
        _publishPacket = publishPacket ?? (static (character, packet) => character.SendPacket(packet));
        _characterResolver = characterResolver ?? (id => WorldManager.Instance.GetCharacterById(id));
    }

    public void Load()
    {
        var byCharacter = new Dictionary<uint, CharacterButler>();
        var characterByHouse = new Dictionary<uint, uint>();
        foreach (var state in repository.LoadAllStates())
        {
            var butler = new CharacterButler(state.Butler.CharacterId);
            butler.ApplyLoadedState(state);
            byCharacter[state.Butler.CharacterId] = butler;
            if (state.Butler.HouseId != 0)
                characterByHouse[state.Butler.HouseId] = state.Butler.CharacterId;
        }

        lock (_sync)
        {
            _byCharacter.Clear();
            foreach (var (characterId, butler) in byCharacter)
                _byCharacter[characterId] = butler;
            _characterByHouse.Clear();
            foreach (var (houseId, characterId) in characterByHouse)
                _characterByHouse[houseId] = characterId;
            _deletedCharacters.Clear();
        }
    }

    public CharacterButler GetOrCreate(uint characterId)
    {
        lock (_sync)
            return GetOrCreateLocked(characterId);
    }

    public IReadOnlyList<CharacterButler> SnapshotAll()
    {
        lock (_sync)
            return [.. _byCharacter.Values];
    }

    private CharacterButler GetOrCreateLocked(uint characterId)
    {
        if (!_byCharacter.TryGetValue(characterId, out var butler))
            _byCharacter[characterId] = butler = new CharacterButler(characterId);
        return butler;
    }

    public ButlerOperationResult Bind(Character character, House house, Func<uint, House> registeredHouseResolver,
        bool notifyOwner = false) =>
        WithPersistenceOperation(() => BindUnderPersistenceGate(character, house, registeredHouseResolver,
            notifyOwner));

    private ButlerOperationResult BindUnderPersistenceGate(Character character, House house,
        Func<uint, House> registeredHouseResolver, bool notifyOwner)
    {
        if (character == null || house == null)
            return new ButlerOperationResult(false, ErrorMessageType.InteractionPermissionDeny);

        lock (house.LifecycleSyncRoot)
        {
            if (house.IsRemovedFromWorld || !ReferenceEquals(registeredHouseResolver?.Invoke(house.Id), house) ||
                house.OwnerId != character.Id || house.CurrentStep != -1)
                return new ButlerOperationResult(false, ErrorMessageType.InteractionPermissionDeny);

            CharacterButler butler;
            lock (_sync)
            {
                if (_deletedCharacters.Contains(character.Id))
                    return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);
                butler = GetOrCreateLocked(character.Id);
                if (_characterByHouse.TryGetValue(house.Id, out var boundCharacterId) &&
                    boundCharacterId != character.Id)
                    return new ButlerOperationResult(false, ErrorMessageType.AlreadyRequested);
            }

            lock (butler.OperationSyncRoot)
            {
                CharacterButlerRecord record;
                lock (butler.SyncRoot)
                {
                    if (butler.IsDeleted)
                        return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);
                    if (butler.HouseId != 0)
                        return new ButlerOperationResult(false, ErrorMessageType.AlreadyRequested);
                    record = butler.Snapshot() with { HouseId = house.Id };
                }

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

                lock (butler.SyncRoot)
                    butler.Apply(record);

                // The registry lock is an index latch only. Never hold it while waiting for the
                // house, the per-character operation lock, state, or the database.
                lock (_sync)
                    _characterByHouse[house.Id] = character.Id;

                if (notifyOwner)
                {
                    lock (butler.SyncRoot)
                    {
                        _publishPacket(character, new SCButlerBoundPacket(
                            BuildWire(character, butler, character.Id, house.TlId, true),
                            house.Name ?? string.Empty,
                            (ushort)ErrorMessageType.NoErrorMessage));
                    }
                }
                return new ButlerOperationResult(true, ErrorMessageType.NoErrorMessage);
            }
        }
    }

    public ButlerOperationResult Unbind(Character character, bool notifyOwner = false) =>
        WithPersistenceOperation(() => UnbindUnderPersistenceGate(character, notifyOwner));

    private ButlerOperationResult UnbindUnderPersistenceGate(Character character, bool notifyOwner)
    {
        if (character == null)
            return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);

        CharacterButler butler;
        lock (_sync)
        {
            if (_deletedCharacters.Contains(character.Id))
                return new ButlerOperationResult(false, ErrorMessageType.InvalidTarget);
            butler = GetOrCreateLocked(character.Id);
        }

        lock (butler.OperationSyncRoot)
        {
            var result = UnbindLocked(butler, 0, character);
            if (result.Success && notifyOwner)
            {
                lock (butler.SyncRoot)
                    _publishPacket(character,
                        new SCButlerUnboundPacket((ushort)ErrorMessageType.NoErrorMessage));
            }
            return result;
        }
    }

    public bool UnbindHouse(uint houseId, bool notifyOwner = true) =>
        WithPersistenceOperation(() => UnbindHouseUnderPersistenceGate(houseId, notifyOwner));

    private bool UnbindHouseUnderPersistenceGate(uint houseId, bool notifyOwner)
    {
        if (houseId == 0)
            return true;

        uint characterId;
        CharacterButler butler;
        lock (_sync)
        {
            if (!_characterByHouse.TryGetValue(houseId, out characterId))
                return true;
            if (!_byCharacter.TryGetValue(characterId, out butler))
                return false;
        }

        var owner = _characterResolver(characterId);
        ButlerOperationResult result;
        lock (butler.OperationSyncRoot)
        {
            lock (butler.SyncRoot)
            {
                if (butler.HouseId != houseId)
                {
                    lock (_sync)
                    {
                        if (_characterByHouse.TryGetValue(houseId, out var indexedCharacterId) &&
                            indexedCharacterId == characterId)
                            _characterByHouse.Remove(houseId);
                    }
                    return true;
                }
            }

            result = UnbindLocked(butler, houseId, owner);
            if (result.Success && notifyOwner && owner != null)
            {
                lock (butler.SyncRoot)
                    _publishPacket(owner,
                        new SCButlerUnboundPacket((ushort)ErrorMessageType.NoErrorMessage));
            }
        }
        return result.Success;
    }

    private ButlerOperationResult UnbindLocked(CharacterButler butler, uint expectedHouseId, Character owner)
    {
        var result = _unbindService.UnbindLocked(butler, expectedHouseId, owner);
        if (!result.Success)
            return new ButlerOperationResult(false, result.Error);

        lock (_sync)
        {
            if (result.PreviousHouseId != 0 &&
                _characterByHouse.TryGetValue(result.PreviousHouseId, out var characterId) &&
                characterId == butler.CharacterId)
                _characterByHouse.Remove(result.PreviousHouseId);
        }

        return new ButlerOperationResult(true, ErrorMessageType.NoErrorMessage);
    }

    public void RemoveCharacter(uint characterId) =>
        WithPersistenceOperation(() =>
        {
            RemoveCharacterUnderPersistenceGate(characterId);
            return true;
        });

    private void RemoveCharacterUnderPersistenceGate(uint characterId)
    {
        CharacterButler butler;
        lock (_sync)
        {
            if (_deletedCharacters.Contains(characterId))
                return;
            butler = GetOrCreateLocked(characterId);
        }

        lock (butler.OperationSyncRoot)
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

            uint houseId;
            lock (butler.SyncRoot)
            {
                houseId = butler.HouseId;
                butler.IsDeleted = true;
            }

            lock (_sync)
            {
                _deletedCharacters.Add(characterId);
                if (houseId != 0 && _characterByHouse.TryGetValue(houseId, out var ownerId) &&
                    ownerId == characterId)
                    _characterByHouse.Remove(houseId);
                if (_byCharacter.TryGetValue(characterId, out var registered) && ReferenceEquals(registered, butler))
                    _byCharacter.Remove(characterId);
            }
        }
    }

    public ButlerPresentation GetPresentation(Character character)
        => GetPresentation(character, HousingManager.Instance.GetHouseById, null);

    public ButlerPresentation GetPresentation(Character character, Action<ButlerPresentation> publish)
        => GetPresentation(character, HousingManager.Instance.GetHouseById, publish);

    internal ButlerPresentation GetPresentation(Character character, Func<uint, House> houseResolver)
        => GetPresentation(character, houseResolver, null);

    internal ButlerPresentation GetPresentation(
        Character character,
        Func<uint, House> houseResolver,
        Action<ButlerPresentation> publish)
    {
        CharacterButler butler = null;
        var deleted = false;
        lock (_sync)
        {
            if (_deletedCharacters.Contains(character.Id))
                deleted = true;
            else
                butler = GetOrCreateLocked(character.Id);
        }
        if (deleted)
            return PublishPresentation(
                new ButlerPresentation(
                    false, CharacterButler.ResetWire, string.Empty, ErrorMessageType.InvalidTarget),
                publish);

        while (true)
        {
            uint expectedHouseId;
            lock (butler.SyncRoot)
            {
                expectedHouseId = butler.HouseId;
                if (expectedHouseId == 0)
                    return PublishPresentation(
                        new ButlerPresentation(false, BuildWire(null, butler, 0, 0, false), string.Empty),
                        publish);
            }

            var house = houseResolver(expectedHouseId);
            if (house == null)
            {
                var cleanup = UnbindExpectedAndPublish(butler, expectedHouseId, character, publish);
                if (cleanup.HasValue)
                    return cleanup.Value;
                continue;
            }

            var clearStaleAssociation = false;
            lock (house.LifecycleSyncRoot)
            {
                if (house.IsRemovedFromWorld || house.Id != expectedHouseId || house.OwnerId != character.Id ||
                    house.CurrentStep != -1)
                    clearStaleAssociation = true;
                else
                {
                    lock (butler.SyncRoot)
                    {
                        if (butler.HouseId != expectedHouseId)
                            continue;
                        return PublishPresentation(new ButlerPresentation(
                            true,
                            BuildWire(character, butler, character.Id, house.TlId, true),
                            house.Name ?? string.Empty), publish);
                    }
                }
            }

            // Release the house lifecycle lock before the durable cleanup enters PersistenceGate.
            // Housing mutations take that gate before the house lock, so reversing the order here
            // would deadlock a concurrent World save or sale.
            if (clearStaleAssociation)
            {
                var cleanup = UnbindExpectedAndPublish(butler, expectedHouseId, character, publish);
                if (cleanup.HasValue)
                    return cleanup.Value;
            }
        }
    }

    private ButlerPresentation? UnbindExpectedAndPublish(
        CharacterButler butler,
        uint expectedHouseId,
        Character owner,
        Action<ButlerPresentation> publish)
    {
        return WithPersistenceOperation<ButlerPresentation?>(() =>
        {
            ButlerOperationResult result;
            ButlerPresentation presentation;
            lock (butler.OperationSyncRoot)
            {
                lock (butler.SyncRoot)
                    if (butler.HouseId != expectedHouseId)
                        return null;
                result = UnbindLocked(butler, expectedHouseId, owner);

                lock (butler.SyncRoot)
                {
                    presentation = result.Success
                        ? new ButlerPresentation(
                            false, BuildWire(null, butler, 0, 0, false), string.Empty)
                        : new ButlerPresentation(
                            false,
                            BuildWire(null, butler, 0, 0, false),
                            string.Empty,
                            result.Error == ErrorMessageType.NoErrorMessage
                                ? ErrorMessageType.InternalError
                                : result.Error);
                    PublishPresentation(presentation, publish);
                }
            }

            if (!result.Success)
                Logger.Warn("Could not clear stale farmhand house {0} for character {1}", expectedHouseId,
                    butler.CharacterId);
            return presentation;
        });
    }

    private static ButlerPresentation PublishPresentation(
        ButlerPresentation presentation,
        Action<ButlerPresentation> publish)
    {
        publish?.Invoke(presentation);
        return presentation;
    }

    private ButlerInfoWire BuildWire(
        Character character,
        CharacterButler butler,
        ulong ownerId,
        ushort houseTlId,
        bool includeResidenceState)
    {
        var wire = ButlerInfoWire.Empty(
            ownerId,
            CharacterBlocked.LocalWorldId,
            butler.Name,
            houseTlId,
            butler.LaborPower,
            butler.LpChargedAmount,
            includeResidenceState ? butler.RemainProductionCost : (ushort)0) with
        {
            PermanentDatas = butler.SnapshotPermanentDatas()
        };
        if (!includeResidenceState)
            return wire;

        var bagItems = new List<Item>();
        foreach (var stored in butler.StoredItems.Values)
        {
            var item = _itemManager.GetItemByItemId(stored.ItemId);
            if (stored.Type == (byte)SlotType.Inventory && item != null &&
                item.OwnerId == butler.CharacterId && item.SlotType == SlotType.System &&
                ReferenceEquals(item._holdingContainer, character?.Inventory?.SystemContainer))
                bagItems.Add(item);
            else
                Logger.Warn("Skipping invalid stored farmhand item {0} for character {1}",
                    stored.ItemId, butler.CharacterId);
        }

        var harvestDatas = new Dictionary<long, ButlerHarvestDataWire>();
        foreach (var job in butler.HarvestJobs.Values)
        {
            if (job.RequestedAmount > short.MaxValue || job.RemainingRepeatCount > short.MaxValue)
            {
                Logger.Warn("Skipping invalid farmhand harvest job {0} for character {1}",
                    job.JobId, butler.CharacterId);
                continue;
            }
            harvestDatas[job.JobId] = new ButlerHarvestDataWire(
                job.StaticHarvestId,
                (short)job.RemainingRepeatCount,
                (short)job.RequestedAmount,
                job.LaborPowerForExperience,
                job.UpdateTime);
        }

        return wire with { BagItems = bagItems, HarvestDatas = harvestDatas };
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
        lock (butler.OperationSyncRoot)
        {
            lock (butler.SyncRoot)
            {
                if (butler.IsDeleted)
                    return;
                repository.Save(butler.Snapshot(), connection, transaction);
            }
        }
    }

    private static T WithPersistenceOperation<T>(Func<T> operation)
    {
        var entered = !PersistenceGate.IsOperationHeld;
        if (entered)
            PersistenceGate.EnterOperation();
        try
        {
            return operation();
        }
        finally
        {
            if (entered)
                PersistenceGate.ExitOperation();
        }
    }
}
