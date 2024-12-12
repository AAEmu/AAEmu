using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using System;
using System.IO;

namespace AAEmu.Game.Scripts.Commands;

public class SendPacket : ICommand
{
    public string[] CommandNames { get; set; } = ["packet"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<hex values/file path>";
    }

    public string GetCommandHelpText()
    {
        return "Send packet (hex) to player's character from either a file path or received message";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length != 1)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        string hex;

        var path = Path.Combine(Directory.GetCurrentDirectory(), args[0]);
        if (File.Exists(path))
        {
            CommandManager.SendNormalText(this, messageOutput, "File: " + args[0]);
            hex = File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        else
        {
            hex = args[0];
        }

        if ((hex.Length & 1) != 0)
        {
            CommandManager.SendErrorText(this, messageOutput,
                "Hex-data must be even in length and greater than 2 bytes");
            return;
        }

        var valid = true;
        foreach (var c in hex)
        {
            valid = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

            if (!valid)
            {
                character.SendMessage("[Packet] " + CommandManager.CommandPrefix + "packet <hex/file path>");
                return;
            }
        }

        var raw = new byte[hex.Length / 2];

        for (var i = 0; i < raw.Length; i++)
        {
            int high = hex[i * 2];
            int low = hex[i * 2 + 1];
            high = (high & 0xf) + ((high & 0x40) >> 6) * 9;
            low = (low & 0xf) + ((low & 0x40) >> 6) * 9;

            raw[i] = (byte)((high << 4) | low);
        }

        var typeId = (ushort)(raw[0] | (raw[1] << 8));
        var payload = new byte[(hex.Length - 4) / 2];
        Array.Copy(raw, 2, payload, 0, payload.Length);

        var p = new SCRawPacket(typeId, payload);

        CommandManager.SendNormalText(this, messageOutput, p.Encode().ToString());
        character.SendPacket(p);
    }
}
