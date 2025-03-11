using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterActability
{
    public Dictionary<uint, Actability> Actabilities { get; set; }

    public Character Owner { get; set; }

    public CharacterActability(Character owner)
    {
        Owner = owner;
        Actabilities = new Dictionary<uint, Actability>();
    }

    /// <summary>
    /// Adds points to a specific ActAbility (life skill)
    /// </summary>
    /// <param name="id"></param>
    /// <param name="point"></param>
    /// <returns>The amount that was actually changed</returns>
    public int AddPoint(uint id, int point)
    {
        if (!Actabilities.ContainsKey(id))
            return 0;

        var actability = Actabilities[id];
        var previousPoints = actability.Point;
        actability.Point += point;

        var template = CharacterManager.Instance.GetExpertLimit(actability.Step);
        if (actability.Point > template.UpLimit)
            actability.Point = template.UpLimit;
        return actability.Point - previousPoints;
    }

    public void Regrade(uint id, bool isUpgrade)
    {
        var actability = Actabilities[id];

        // TODO add validation to expert limit, if expert_limit = 0 -> infinity

        if (isUpgrade)
        {
            var template = CharacterManager.Instance.GetExpertLimit(actability.Step);
            if (template == null)
                return; // TODO ... send msg error?

            if (actability.Point < template.UpLimit)
                return; // TODO ... send msg error?

            actability.Step++;
        }
        else
        {
            var template = CharacterManager.Instance.GetExpertLimit(actability.Step - 1);
            if (template == null)
                return; // TODO ... send msg error?

            actability.Step--;
            actability.Point = template.UpLimit;
        }

        Owner.SendPacket(new SCExpertLimitModifiedPacket(isUpgrade, id, actability.Step));
    }

    public void ExpandExpert()
    {
        var expand = CharacterManager.Instance.GetExpandExpertLimit(Owner.ExpandedExpert);
        if (expand == null)
            return; // TODO ... send msg error?

        if (expand.LifePoint > Owner.VocationPoint)
        {
            Owner.SendErrorMessage(ErrorMessageType.NotEnoughExpandItemAndMoney);
            return; // TODO ... send msg error?
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0 && !Owner.Inventory.CheckItems(Items.SlotType.Bag, expand.ItemId, expand.ItemCount))
        {
            Owner.SendErrorMessage(ErrorMessageType.NotEnoughExpandItem);
            return; // TODO ... send msg error?
        }

        if (expand.LifePoint > 0)
        {
            Owner.ChangeGamePoints(GamePointKind.Vocation, expand.LifePoint);
        }

        if (expand.ItemId != 0 && expand.ItemCount != 0)
        {
            Owner.Inventory.Bag.ConsumeItem(ItemTaskType.ExpandExpert, expand.ItemId, expand.ItemCount, null);
            /*
            var items = Owner.Inventory.RemoveItem(expand.ItemId, expand.ItemCount);

            var tasks = new List<ItemTask>();
            foreach (var (item, count) in items)
            {
                if (item.Count == 0)
                    tasks.Add(new ItemRemove(item));
                else
                    tasks.Add(new ItemCountUpdate(item, -count));
            }

            Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ExpandExpert, tasks, new List<ulong>()));
            */
        }

        Owner.ExpandedExpert = expand.ExpandCount;
        Owner.SendPacket(new SCExpertExpandedPacket(Owner.ExpandedExpert));
    }

    public void Send()
    {
        Owner.SendPacket(new SCActabilityPacket(true, Actabilities.Values.ToArray()));
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM actabilities WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    var template = CharacterManager.Instance.GetActability(id);

                    var actability = new Actability(template)
                    {
                        Id = id,
                        Point = reader.GetInt32("point"),
                        Step = reader.GetByte("step")
                    };
                    Actabilities.Add(actability.Id, actability);
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var actability in Actabilities.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO actabilities(`id`,`point`,`step`,`owner`) VALUES (@id, @point, @step, @owner)";
                command.Parameters.AddWithValue("@id", (byte)actability.Id);
                command.Parameters.AddWithValue("@point", actability.Point);
                command.Parameters.AddWithValue("@step", actability.Step);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }
}
