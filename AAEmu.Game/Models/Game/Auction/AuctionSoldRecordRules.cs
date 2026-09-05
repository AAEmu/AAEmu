namespace AAEmu.Game.Models.Game.Auction;

public readonly record struct AuctionSale(uint ItemTemplateId, byte Grade, DateTime SoldAt, long Price, int Stack);

/// <summary>
/// Builds the fourteen-day sold-record page the client always reads.
/// </summary>
public static class AuctionSoldRecordRules
{
    public static IReadOnlyList<AuctionSoldRecord> BuildDays(
        IEnumerable<AuctionSale> sales,
        uint templateId,
        byte grade,
        DateTime utcNow)
    {
        var day0 = utcNow.Date;
        var rows = new AuctionSoldRecord[AuctionHouseRules.SoldRecordDays];
        for (var day = 0; day < rows.Length; day++)
        {
            rows[day] = new AuctionSoldRecord
            {
                Day = day,
                ItemTemplateId = templateId,
                Grade = grade
            };
        }

        foreach (var sale in sales.OrderBy(s => s.SoldAt))
        {
            if (sale.ItemTemplateId != templateId || sale.Grade != grade)
                continue;

            var day = (int)(day0 - sale.SoldAt.Date).TotalDays;
            if (day < 0 || day >= rows.Length)
                continue;

            var row = rows[day];
            var unit = sale.Stack > 0 ? sale.Price / sale.Stack : sale.Price;
            if (row.Volume == 0)
            {
                row.MinPrice = unit;
                row.MaxPrice = unit;
                row.AveragePrice = unit;
                row.LastPrice = unit;
                row.Volume = sale.Stack;
                continue;
            }

            row.MinPrice = Math.Min(row.MinPrice, unit);
            row.MaxPrice = Math.Max(row.MaxPrice, unit);
            var total = row.AveragePrice * row.Volume + unit * sale.Stack;
            row.Volume += sale.Stack;
            row.AveragePrice = row.Volume > 0 ? total / row.Volume : 0;
            row.LastPrice = unit;
        }

        return rows;
    }
}
