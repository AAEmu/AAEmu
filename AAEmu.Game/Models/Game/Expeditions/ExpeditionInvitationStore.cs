using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Expeditions;

internal sealed class ExpeditionInvitationStore
{
    private readonly Dictionary<uint, ExpeditionInvitation> _invitations = [];
    private readonly object _sync = new();

    public void Clear()
    {
        lock (_sync)
            _invitations.Clear();
    }

    public void Set(uint candidateId, ExpeditionInvitation invitation)
    {
        lock (_sync)
            _invitations[candidateId] = invitation;
    }

    public bool TryConsume(uint candidateId, object candidateSession, FactionsEnum expeditionId, uint inviterId, out ExpeditionInvitation invitation)
    {
        lock (_sync)
        {
            if (!_invitations.TryGetValue(candidateId, out invitation) ||
                invitation.ExpeditionId != expeditionId ||
                invitation.InviterId != inviterId ||
                !ReferenceEquals(invitation.CandidateSession, candidateSession))
                return false;

            _invitations.Remove(candidateId);
            return true;
        }
    }

    public void RemoveFor(uint characterId, object session)
    {
        lock (_sync)
        {
            foreach (var candidateId in _invitations
                         .Where(x => (x.Key == characterId && ReferenceEquals(x.Value.CandidateSession, session)) ||
                                     (x.Value.InviterId == characterId && ReferenceEquals(x.Value.InviterSession, session)))
                         .Select(x => x.Key)
                         .ToArray())
                _invitations.Remove(candidateId);
        }
    }
}

internal sealed record ExpeditionInvitation(
    FactionsEnum ExpeditionId,
    uint InviterId,
    FactionsEnum MotherId,
    object InviterSession,
    object CandidateSession);
