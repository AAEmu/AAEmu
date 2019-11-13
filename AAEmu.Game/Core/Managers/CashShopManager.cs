using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    class CashShopManager : Singleton<CashShopManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<CashShopItem> _cashShopItem;
        private Dictionary<uint, CashShopItemDetail> _cashShopItemDetail;

        public void Load()
        {
            _cashShopItem = new List<CashShopItem>();
            _cashShopItemDetail = new Dictionary<uint, CashShopItemDetail>();

            using (var ctx = new GameDBContext())
            {
                var items = ctx.CashShopItem.ToList();
                _cashShopItem = items.Select(c => (CashShopItem)c).ToList();
                _cashShopItemDetail = items.ToDictionary(c => (uint)c.Id, c => (CashShopItemDetail)c);
            }
        }

        public List<CashShopItem> GetCashShopItems()
        {
            return _cashShopItem;
        }

        public CashShopItem GetCashShopItem(uint cashShopId)
        {
            return _cashShopItem.Find(a => a.CashShopId == cashShopId);
        }

        public List<CashShopItem> GetCashShopItems(sbyte mainTab,sbyte subTab,ushort page)
        {
            return _cashShopItem.FindAll(a=>a.MainTab==mainTab && a.SubTab == subTab);
        }

        public CashShopItemDetail GetCashShopItemDetail(uint cashShopId)
        {
            return _cashShopItemDetail.ContainsKey(cashShopId) ? _cashShopItemDetail[cashShopId] : new CashShopItemDetail();
        }
    }
}
