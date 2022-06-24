using System;
using System.Collections.Generic;
using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Core.Network.Connections
{
    public interface IGameConnection
    {
        uint AccountId { get; set; }
        ICharacter ActiveChar { get; set; }
        uint Id { get; }
        IPAddress Ip { get; }
        PacketStream LastPacket { get; set; }
        DateTime LastPing { get; set; }
        Task LeaveTask { get; set; }
        int PacketCount { get; set; }
        AccountPayment Payment { get; set; }
        GameState State { get; set; }
        List<IDisposable> Subscribers { get; set; }
        Dictionary<uint, ICharacter> Characters { get; set; }
        Dictionary<uint, House> Houses { get; set; }
        void AddAttribute(string name, object value);
        object GetAttribute(string name);
        void LoadAccount();
        void OnConnect();
        void OnDisconnect();
        void PushSubscriber(IDisposable disposable);
        void SaveAndRemoveFromWorld();
        void SendPacket(byte[] packet);
        void SendPacket(GamePacket packet);
        void Shutdown();
    }
}
