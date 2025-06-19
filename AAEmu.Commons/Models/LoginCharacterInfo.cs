using AAEmu.Commons.Network;

namespace AAEmu.Commons.Models;

/// <summary>
/// 表示登录时角色信息的类，用于网络数据包的编组。
/// </summary>
public class LoginCharacterInfo : PacketMarshaler
{
    /// <summary>
    /// 获取或设置角色的唯一标识符。
    /// </summary>
    public uint Id { get; set; }
    /// <summary>
    /// 获取或设置角色所属账户的标识符。
    /// </summary>
    public uint AccountId { get; set; }
    /// <summary>
    /// 获取或设置角色所在游戏服务器的标识符。
    /// </summary>
    public byte GsId { get; set; }
    /// <summary>
    /// 获取或设置角色的名称。
    /// </summary>
    public string Name { get; set; }
    /// <summary>
    /// 获取或设置角色的种族。
    /// </summary>
    public byte Race { get; set; }
    /// <summary>
    /// 获取或设置角色的性别。
    /// </summary>
    public byte Gender { get; set; }

    /// <summary>
    /// 从数据包流中读取角色信息。
    /// </summary>
    /// <param name="stream">包含角色数据的数据包流。</param>
    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt32();
        AccountId = stream.ReadUInt32();
        Name = stream.ReadString();
        Race = stream.ReadByte();
        Gender = stream.ReadByte();
    }

    /// <summary>
    /// 将角色信息写入数据包流。
    /// </summary>
    /// <param name="stream">要写入角色数据的数据包流。</param>
    /// <returns>写入数据后的数据包流。</returns>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(AccountId);
        stream.Write(Name);
        stream.Write(Race);
        stream.Write(Gender);
        return stream;
    }
}
