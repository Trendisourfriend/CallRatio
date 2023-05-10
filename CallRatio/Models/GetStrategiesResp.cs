using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StraddleStrategy.Models.Response
{
    public class GetStrategiesResp
    {
        public long StrategyId { get; set; }
        public long SubscriptionId { get; set; }
        public string TradeDetails { get; set; }
        public long UserId { get; set; }
        public string PrimaryKey { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string TOTP { get; set; }
        public OrderDetails EntryBuy { get; set; }
        public OrderDetails ExitBuy { get; set; }
        public OrderDetails EntrySell { get; set; }
        public OrderDetails ExitSell { get; set; }
        public string Exchange { get; set; }
    }
    public class OrderDetails
    {
        public long OrderId { get; set; }
        public string Price { get; set; }
        public string Symbol { get; set; }
        public string SymbolToken { get; set; }
        public string SellPrice { get; set; }
    }
}
