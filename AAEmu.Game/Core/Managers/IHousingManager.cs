using System.Numerics;

using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IHousingManager
{
    int GetByAccountId(Dictionary<uint, House> values, uint accountId);
    int GetByCharacterId(Dictionary<uint, House> values, uint characterId);
    void LoadPlayerHousing(WorldInstance worldInstance);
    void SpawnAll();
    void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z);
    void HouseTaxInfo(GameConnection connection, ushort tlId);
    void Build(GameConnection connection, uint designId, float posX, float posY, float posZ, float zRot, ulong itemId, int moneyAmount, int ht, bool autoUseAaPoint);
    void ChangeHousePermission(GameConnection connection, ushort tlId, HousingPermission permission);
    void ChangeHouseName(GameConnection connection, ushort tlId, string name);
    void Demolish(GameConnection connection, House house, bool failedToPayTax, bool forceRestoreAllDecor);
    void RemoveDeadHouse(House house);
    bool CalculateBuildingTaxInfo(uint accountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate, out int oneWeekTaxCount);
    House GetHouseById(uint houseId);
    void UpdateOwnedHousingFaction(uint characterId, FactionsEnum factionId);
    bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller);
    bool CancelForSale(ushort houseTlId, bool returnCertificates = true);
    bool BuyHouse(ushort houseTlId, uint money, Character character);
    void CheckHousingTaxes();
    void UpdateTaxInfo(House house);
    bool DecorateHouse(Character player, ushort houseTlId, uint designId, Vector3 pos, Quaternion quat, uint parentObjId, ulong itemId);
    void HousingToggleAllowRecover(Character character, ushort houseTl);
    House GetHouseAtLocation(float x, float y);
    bool PayWeeklyTax(House house);
    (int, int) Save(MySqlConnection connection, MySqlTransaction transaction);
    void ReconcileBoundDoodads();
}
