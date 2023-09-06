using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// 此表来自于代码中的字段并去除重复字段生成。字段名称和内容以代码为准。
/// </summary>
public partial class CashShopItem
{
    /// <summary>
    /// shop_id
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// 唯一ID
    /// </summary>
    public uint? UniqId { get; set; }

    /// <summary>
    /// 出售名称
    /// </summary>
    public string CashName { get; set; }

    /// <summary>
    /// 主分类1-6
    /// </summary>
    public byte? MainTab { get; set; }

    /// <summary>
    /// 子分类1-7
    /// </summary>
    public byte? SubTab { get; set; }

    /// <summary>
    /// 等级限制
    /// </summary>
    public byte? LevelMin { get; set; }

    /// <summary>
    /// 等级限制
    /// </summary>
    public byte? LevelMax { get; set; }

    /// <summary>
    /// 物品模板id
    /// </summary>
    public uint? ItemTemplateId { get; set; }

    /// <summary>
    /// 是否出售
    /// </summary>
    public byte? IsSell { get; set; }

    /// <summary>
    /// 是否隐藏
    /// </summary>
    public byte? IsHidden { get; set; }

    public byte? LimitType { get; set; }

    public ushort? BuyCount { get; set; }

    public byte? BuyType { get; set; }

    public uint? BuyId { get; set; }

    /// <summary>
    /// 出售开始
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 出售截止
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 货币类型
    /// </summary>
    public byte? Type { get; set; }

    /// <summary>
    /// 价格
    /// </summary>
    public uint? Price { get; set; }

    /// <summary>
    /// 剩余数量
    /// </summary>
    public uint? Remain { get; set; }

    /// <summary>
    /// 赠送类型
    /// </summary>
    public uint? BonusType { get; set; }

    /// <summary>
    /// 赠送数量
    /// </summary>
    public uint? BounsCount { get; set; }

    /// <summary>
    /// 是否限制一人一次
    /// </summary>
    public byte? CmdUi { get; set; }

    /// <summary>
    /// 捆绑数量
    /// </summary>
    public uint? ItemCount { get; set; }

    public byte? SelectType { get; set; }

    public byte? DefaultFlag { get; set; }

    /// <summary>
    /// 活动类型
    /// </summary>
    public byte? EventType { get; set; }

    /// <summary>
    /// 活动时间
    /// </summary>
    public DateTime? EventDate { get; set; }

    /// <summary>
    /// 当前售价
    /// </summary>
    public uint? DisPrice { get; set; }
}
