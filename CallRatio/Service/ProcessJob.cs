using CallRatio.Interface;
using IService.Models.Response;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrderService;
using OrderService.AngleLibrary;
using StraddleStrategy.Models.Response;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CallRatio.Service
{
    public class ProcessJob : IProcessJob
    {
        List<SymbolModel> symbols;
        private readonly IConfiguration _configuration;
        public ProcessJob(IConfiguration configuration)
        {
            string json = "";
            string symbolAngleURL = "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";

            using (WebClient wc = new WebClient())
            {
                json = wc.DownloadString(symbolAngleURL);
            }


            //json = new WebClient().DownloadString(symbolAngleURL);
            symbols = JsonConvert.DeserializeObject<List<SymbolModel>>(json);
            _configuration = configuration;

        }

        public async Task<Boolean> ProcessStart()
        {
            try
            {
                List<GetStrategiesResp> Strategies = new List<GetStrategiesResp>();
                Strategies = GetStrategies();
                if (Strategies.Any())
                {
                    DateTime current = DateTime.Now;
                    TimeSpan timestart = new TimeSpan(09, 15, 00);
                    TimeSpan timeend = new TimeSpan(15, 30, 00);
                    while (timestart <= current.TimeOfDay && current.TimeOfDay <= timeend)
                    {
                        foreach (GetStrategiesResp strategy in Strategies)
                        {
                           
                            //JToken JtradeSymbol = ;
                            dynamic symbolData = JObject.Parse(strategy.TradeDetails);

                            string tradeSymbol = symbolData?.symbol?.ToString();
                            long Points = symbolData?.Points;
                            int quantity = QuanityCalc(tradeSymbol, symbolData?.quantity?.ToString());

                            SmartAPICalls smartAPICalls = new SmartAPICalls(strategy.PrimaryKey, strategy.UserName, strategy.Password, strategy.TOTP);
                            if (strategy?.EntryBuy?.OrderId == 0 && strategy?.EntrySell?.OrderId == 0)
                            {
                                await EntryOrderProcess(tradeSymbol, quantity, smartAPICalls, strategy, Points);
                            }
                            else
                            {
                                if (!smartAPICalls.session && !smartAPICalls.smart)
                                    smartAPICalls = new SmartAPICalls(strategy.PrimaryKey, strategy.UserName, strategy.Password, strategy.TOTP);

                                LTPDataRequest lTPDataRequest = new LTPDataRequest();
                                SymbolModel symbol = symbols.Where(x => x.exch_seg == "NFO" && x.name == tradeSymbol && x.symbol.Contains("FUT")).OrderBy(y => Convert.ToDateTime(y.expiry)).FirstOrDefault();
                                lTPDataRequest.symboltoken = symbol.token;
                                lTPDataRequest.tradingsymbol = symbol.symbol;
                                lTPDataRequest.exchange = symbol.exch_seg;
                                var ltpResp = await smartAPICalls.GetLTPData(lTPDataRequest);
                                string atm = (Math.Floor((Convert.ToDecimal(ltpResp?.GetLTPDataResponse?.data?.ltp) / StrikeRound(tradeSymbol))) * StrikeRound(tradeSymbol)).ToString();
                                if(Convert.ToInt64(atm) == Convert.ToInt64(strategy?.EntrySell?.Price))
                                {
                                    if (!smartAPICalls.session && !smartAPICalls.smart)
                                        smartAPICalls = new SmartAPICalls(strategy.PrimaryKey, strategy.UserName, strategy.Password, strategy.TOTP);

                                    OutputBaseClass OnePositions = await smartAPICalls.OpenPosition().ConfigureAwait(false);
                                    while (!OnePositions.status)
                                    {
                                        OnePositions = await smartAPICalls.OpenPosition().ConfigureAwait(false);
                                    }

                                    var buyPosition = OnePositions.status ? OnePositions?.GetPositionResponse?.data?.Where(x => !x.netqty.Equals("0") && x.symboltoken.Equals(strategy?.EntryBuy?.SymbolToken)) : null;
                                    string? buyQuantity = buyPosition != null && buyPosition.Any() ? buyPosition?.ToList()?.FirstOrDefault()?.netqty : "0";

                                    var sellPosition = OnePositions.status ? OnePositions?.GetPositionResponse?.data?.Where(x => !x.netqty.Equals("0") && x.symboltoken.Equals(strategy?.EntrySell?.SymbolToken)) : null;
                                    string? sellQuantity = sellPosition != null && sellPosition.Any() ? sellPosition?.ToList()?.FirstOrDefault()?.netqty : "0";
                                    
                                        bool isExitSuccessful = await ExitOrderProcess(smartAPICalls, strategy, buyQuantity, sellQuantity, quantity);
                                        if (isExitSuccessful)
                                            await EntryOrderProcess(tradeSymbol, quantity, smartAPICalls, strategy, Points);

                                }
                            }

                        }
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }
            return true;
        }
        public int StrikeRound(string symbol)
        {
            return symbol == "BANKNIFTY" ? 100 : 50;
        }
        public int QuanityCalc(string symbol, string lot)
        {
            switch (symbol)
            {
                case "NIFTY":
                    return Convert.ToInt32(lot) * 50;
                case "BANKNIFTY":
                    return Convert.ToInt32(lot) * 25;
                case "FINNIFTY":
                    return Convert.ToInt32(lot) * 40;
            }
            return 0;

        }
        public OrderInfo GetOrderInfo(string symbol, string token, int quantity, string exchange, bool isSell, bool isSL, string triggerPrice = "", string Price = "")
        {
            OrderInfo orderInfo = new OrderInfo();

            orderInfo.orderid = "";
            orderInfo.variety = isSL ? "STOPLOSS" : "NORMAL";
            orderInfo.tradingsymbol = symbol;
            orderInfo.symboltoken = token;
            orderInfo.transactiontype = isSell ? "SELL" : "BUY";
            orderInfo.exchange = exchange;
            orderInfo.ordertype = isSL ? "STOPLOSS_LIMIT" : "MARKET";
            orderInfo.producttype = "CARRYFORWARD";
            orderInfo.duration = "DAY";
            orderInfo.price = Price == "" ? "0" : Price;
            orderInfo.squareoff = "";
            orderInfo.stoploss = "";
            orderInfo.quantity = quantity.ToString();
            orderInfo.triggerprice = triggerPrice;
            orderInfo.trailingStopLoss = "";
            orderInfo.disclosedquantity = "";
            orderInfo.ordertag = "";
            return orderInfo;

        }
        public async Task<Boolean> EntryOrderProcess(string tradeSymbol, int quantity, SmartAPICalls smartAPICalls, GetStrategiesResp strategy, long Points)
        {
            try
            {
                OrderInfo orderInfo = new OrderInfo();
                LTPDataRequest lTPDataRequest = new LTPDataRequest();
                SymbolModel symbol = symbols.Where(x => x.exch_seg == "NFO" && x.name == tradeSymbol && x.symbol.Contains("FUT")).OrderBy(y => Convert.ToDateTime(y.expiry)).FirstOrDefault();
                lTPDataRequest.symboltoken = symbol.token;
                lTPDataRequest.tradingsymbol = symbol.symbol;
                lTPDataRequest.exchange = symbol.exch_seg;
                var ltpResp = await smartAPICalls.GetLTPData(lTPDataRequest);
                string atm = (Math.Floor((Convert.ToDecimal(ltpResp?.GetLTPDataResponse?.data?.ltp) / StrikeRound(tradeSymbol))) * StrikeRound(tradeSymbol)).ToString();
                long SymbolPrice = Convert.ToInt64(atm);
                List<SymbolModel> atmsymbol = symbols.Where(x => x.exch_seg == "NFO" && x.name == tradeSymbol && x.symbol.Contains(SymbolPrice.ToString()) && x.symbol.EndsWith("CE")).OrderBy(y => Convert.ToDateTime(y.expiry)).ToList();
                if (atmsymbol.Count > 1)
                {
                    strategy.EntryBuy.SymbolToken = atmsymbol[0].token;
                    strategy.EntryBuy.Symbol = atmsymbol[0].symbol;
                    strategy.Exchange = atmsymbol[0].exch_seg;

                    //1st Leg
                    orderInfo = GetOrderInfo(atmsymbol[0].symbol, atmsymbol[0].token, quantity, atmsymbol[0].exch_seg, false, false, "", "");
                    OutputBaseClass entryBuy = await smartAPICalls.PlaceOrder(orderInfo);

                    strategy.EntryBuy.OrderId = entryBuy.status ? entryBuy.PlaceOrderResponse.data.orderid : 0;
                    strategy.EntryBuy.Price = SymbolPrice.ToString();
                }

                SymbolPrice = Convert.ToInt64(atm) + Points;
                atmsymbol = symbols.Where(x => x.exch_seg == "NFO" && x.name == tradeSymbol && x.symbol.Contains(SymbolPrice.ToString()) && x.symbol.EndsWith("CE")).OrderBy(y => Convert.ToDateTime(y.expiry)).ToList();
                if (atmsymbol.Count > 1)
                {
                    strategy.EntrySell.SymbolToken = atmsymbol[0].token;
                    strategy.EntrySell.Symbol = atmsymbol[0].symbol;
                    strategy.Exchange = atmsymbol[0].exch_seg;

                    //1st Leg
                    orderInfo = GetOrderInfo(atmsymbol[0].symbol, atmsymbol[0].token, quantity * 2, atmsymbol[0].exch_seg, true, false, "", "");
                    OutputBaseClass entryBuy = await smartAPICalls.PlaceOrder(orderInfo);

                    strategy.EntrySell.OrderId = entryBuy.status ? entryBuy.PlaceOrderResponse.data.orderid : 0;
                    strategy.EntrySell.SellPrice = SymbolPrice.ToString();
                }

                if (strategy?.EntryBuy.OrderId != 0 && strategy?.EntrySell.OrderId != 0)
                {
                    OutputBaseClass positions = await smartAPICalls.GetTradeBook().ConfigureAwait(false);

                    strategy.ExitBuy.Price = positions.status ? Convert.ToDecimal(positions?.GetTradeBookResponse?.data?.Find(trade => Convert.ToInt64(trade?.orderid) == strategy?.ExitBuy?.OrderId)?.fillprice).ToString() : "0";
                    strategy.ExitSell.Price = positions.status ? Convert.ToDecimal(positions?.GetTradeBookResponse?.data?.Find(trade => Convert.ToInt64(trade?.orderid) == strategy?.ExitSell?.OrderId)?.fillprice).ToString() : "0";
                    
                    string jsonconvert = JsonConvert.SerializeObject(strategy);
                    var res = SqlConn2("member.SaveStrategyOrders", "@OrdersData", jsonconvert);

                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

        }
        public async Task<bool> ExitOrderProcess(SmartAPICalls smartAPICalls, GetStrategiesResp strategy, string buyQuantity, string sellQuantity, int quantity)
        {
            try
            {
                OutputBaseClass exitOrderResult = null;
                OrderInfo orderInfo = new OrderInfo();
                int quant = 0;
                OutputBaseClass orderbook = await smartAPICalls.GetOrderBook().ConfigureAwait(false);
                var EntryBuyOrder = orderbook.status ? orderbook?.GetOrderBookResponse?.data?.Find(trade => Convert.ToInt64(trade.orderid) == strategy?.EntryBuy.OrderId) : null;

                var EntrySellOrder = orderbook.status ? orderbook?.GetOrderBookResponse?.data?.Find(trade => Convert.ToInt64(trade.orderid) == strategy?.EntrySell.OrderId) : null;
                //Orders Exit

                if (EntryBuyOrder != null && EntrySellOrder != null && EntryBuyOrder.status.ToLower() == "open" && EntrySellOrder.status.ToLower() == "open")
                {
                    if (Convert.ToInt32(buyQuantity) != 0)
                    {
                        int qty = Convert.ToInt32(buyQuantity) >= quantity ? quantity : Convert.ToInt32(buyQuantity);

                        quant = Convert.ToInt32(quantity) > 0 ? Convert.ToInt32(quantity) : Convert.ToInt32(quantity) - (Convert.ToInt32(quantity) * 2);

                        orderInfo = GetOrderInfo(strategy?.EntryBuy?.Symbol, strategy?.EntryBuy?.SymbolToken, quant, strategy.Exchange, true, false, "", "");

                        exitOrderResult = await smartAPICalls.PlaceOrder(orderInfo);
                        strategy.ExitBuy.OrderId = exitOrderResult.status ? exitOrderResult.PlaceOrderResponse.data.orderid : 0;

                    }
                    if (Convert.ToInt32(sellQuantity) != 0)
                    {
                        quantity *= 2;
                        int qty = Convert.ToInt32(buyQuantity) >= quantity ? quantity : Convert.ToInt32(buyQuantity);

                        quant = Convert.ToInt32(quantity) > 0 ? Convert.ToInt32(quantity) : Convert.ToInt32(quantity) - (Convert.ToInt32(quantity) * 2);

                        orderInfo = GetOrderInfo(strategy?.EntryBuy?.Symbol, strategy?.EntryBuy?.SymbolToken, quant, strategy.Exchange, false, false, "", "");

                        exitOrderResult = await smartAPICalls.PlaceOrder(orderInfo);
                        strategy.ExitSell.OrderId = exitOrderResult.status ? exitOrderResult.PlaceOrderResponse.data.orderid : 0;

                    }
                }
                if (strategy?.ExitBuy.OrderId != 0 && strategy?.ExitSell.OrderId != 0)
                {
                    OutputBaseClass positions = await smartAPICalls.GetTradeBook().ConfigureAwait(false);

                    strategy.ExitBuy.Price = positions.status ? Convert.ToDecimal(positions?.GetTradeBookResponse?.data?.Find(trade => Convert.ToInt64(trade?.orderid) == strategy?.ExitBuy?.OrderId)?.fillprice).ToString() : "0";
                    strategy.ExitSell.Price = positions.status ? Convert.ToDecimal(positions?.GetTradeBookResponse?.data?.Find(trade => Convert.ToInt64(trade?.orderid) == strategy?.ExitSell?.OrderId)?.fillprice).ToString() : "0";
                    
                    string jsonconvert = JsonConvert.SerializeObject(strategy);
                    var res = SqlConn2("member.SaveStrategyOrders", "@OrdersData", jsonconvert);

                    strategy.EntryBuy = new OrderDetails();
                    strategy.EntrySell = new OrderDetails();
                    strategy.ExitBuy = new OrderDetails();
                    strategy.ExitSell = new OrderDetails();
                    return true;
                }
                return false;

            }
            catch (Exception ex)
            {
                return false;
            }

        }

        public List<GetStrategiesResp> GetStrategies()
        {
            DataTable dataTable = SqlConn("[common].[getRatioStrategies]", "@strategyId", 7);
            List<GetStrategiesResp> getStrategies = new List<GetStrategiesResp>();
            if (dataTable.Rows.Count > 0)
            {

                getStrategies = (from DataRow dr in dataTable.Rows
                                 select new GetStrategiesResp()
                                 {
                                     StrategyId = Convert.ToInt64(dr["StrategyId"]),
                                     SubscriptionId = Convert.ToInt64(dr["SubscriptionId"]),
                                     TradeDetails = dr["TradeDetails"].ToString(),
                                     UserId = Convert.ToInt64(dr["UserId"]),
                                     PrimaryKey = dr["PrimaryKey"].ToString(),
                                     UserName = dr["UserName"].ToString(),
                                     Password = dr["Password"].ToString(),
                                     TOTP = dr["TOTP"].ToString(),
                                     EntryBuy = dr["Trades"].ToString() != "" ? JsonConvert.DeserializeObject<GetStrategiesResp>(dr["Trades"].ToString()).EntryBuy : new OrderDetails(),
                                     EntrySell = dr["Trades"].ToString() != "" ? JsonConvert.DeserializeObject<GetStrategiesResp>(dr["Trades"].ToString()).EntrySell : new OrderDetails(),
                                     ExitBuy = dr["Trades"].ToString() != "" ? JsonConvert.DeserializeObject<GetStrategiesResp>(dr["Trades"].ToString()).ExitBuy : new OrderDetails(),
                                     ExitSell = dr["Trades"].ToString() != "" ? JsonConvert.DeserializeObject<GetStrategiesResp>(dr["Trades"].ToString()).ExitSell : new OrderDetails(),
                                 }).ToList();

            }
            return getStrategies;
        }
        public DataTable SqlConn(string SPName, string parameter = null, long value = 0)
        {
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DbConnection")))
            {
                conn.Open();
                SqlCommand command = new SqlCommand();
                command.Connection = conn;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = SPName;
                if (parameter != null)
                {
                    command.Parameters.Add(new SqlParameter(parameter, value));
                }
                SqlDataAdapter adapter = new SqlDataAdapter(command);
                DataTable dataTable = new DataTable();
                adapter.Fill(dataTable);
                conn.Close();
                return dataTable;
            }
        }
        public DataTable SqlConn2(string SPName, string parameter = null, string value = null)
        {
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DbConnection")))
            {
                conn.Open();
                SqlCommand command = new SqlCommand();
                command.Connection = conn;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = SPName;
                if (parameter != null)
                {
                    command.Parameters.Add(new SqlParameter(parameter, value));
                }
                SqlDataAdapter adapter = new SqlDataAdapter(command);
                DataTable dataTable = new DataTable();
                adapter.Fill(dataTable);
                conn.Close();
                return dataTable;
            }
        }
    }
}
