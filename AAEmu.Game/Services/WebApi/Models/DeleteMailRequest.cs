namespace AAEmu.Game.Services.WebApi.Models;

public class DeleteMailRequest
{
    public long MailId { get; set; }
    public uint SenderId { get; set; }
    public uint ReceiverId { get; set; }
    public bool TrashItems { get; set; }
}
