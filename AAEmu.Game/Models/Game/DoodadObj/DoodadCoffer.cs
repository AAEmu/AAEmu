using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadCoffer : Doodad
    {
        public int Capacity { get; set; }
        public CofferContainer ItemContainer { get; set; }
        public Character OpenedBy { get; set; }

        public void InitializeCoffer(uint playerId)
        {
            ItemContainer = ItemManager.Instance.NewCofferContainer(playerId);
            ItemContainer.ContainerSize = Capacity;
        }
        
        public override bool AllowRemoval()
        {
            return ((ItemContainer == null) || (ItemContainer.Items.Count <= 0)) && base.AllowRemoval();
        }

        public override void Delete()
        {
            ItemContainer?.Delete();
            base.Delete();
        }

        public override bool AllowedToInteract(Character character)
        {
            var permission = (HousingPermission)Data; 
            if (permission == HousingPermission.Public)
                return base.AllowedToInteract(character);

            // Try to cache the owner Character if it's already in the world to make lookups faster
            var owner = WorldManager.Instance.GetCharacterById(OwnerId);
            
            switch (permission)
            {
                case HousingPermission.Private:
                    if (ItemContainer?.CofferType == ChestType.Otherworldly)
                        return (character.Id == OwnerId) && base.AllowedToInteract(character);
                    else
                    {
                        var ownerAccountId = NameManager.Instance.GetCharaterAccount(OwnerId);
                        return (character.AccountId == ownerAccountId) && base.AllowedToInteract(character);
                    }
                case HousingPermission.Family:
                    var ownerFamily = owner?.Family ?? FamilyManager.Instance.GetFamilyOfCharacter(OwnerId);
                    return ((ownerFamily != 0) && (character.Family != 0) && (ownerFamily == character.Family)) && base.AllowedToInteract(character);
                case HousingPermission.Guild:
                    var ownerGuild = owner?.Expedition?.Id ?? ExpeditionManager.Instance.GetExpeditionOfCharacter(OwnerId);
                    return ((ownerGuild != 0) && (character.Expedition != null) && (character.Expedition.Id != 0) && (character.Expedition.Id == ownerGuild)) && base.AllowedToInteract(character);
                case HousingPermission.Public:
                default:
                    return base.AllowedToInteract(character);
            }
        }

        public override ulong GetItemContainerId()
        {
            return ItemContainer?.ContainerId ?? 0;
        }
    }
}
