using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

/// <summary>Post-lock notification for an already committed account-labor debit.</summary>
public sealed class AccountLaborDebitPublication(Character character, AccountLaborDebit debit)
{
    public void Publish() => character.SendPacket(new SCCharacterLaborPowerChangedPacket(
        debit.LaborDelta, debit.LocalLaborDelta, 0, 0, 0, 0));
}
