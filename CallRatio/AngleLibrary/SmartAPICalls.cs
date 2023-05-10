using OtpNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrderService.AngleLibrary
{

    public class SmartAPICalls
    {
        public readonly string _primarykey;
        public readonly string _username;
        public readonly string _password;
        public readonly bool session;
        public readonly bool smart;
        SmartApi smartapi = null;
        public SmartAPICalls(string primarykey, string username, string password, string TOTP)
        {
            smartapi = new SmartApi(primarykey);
            _primarykey = primarykey;
            _username = username;
            _password = password;
            var bytes = Base32Encoding.ToBytes(TOTP);
            var totp = new Totp(bytes).ComputeTotp();
            session = smartapi.GenerateSession(_username, _password, totp).status;
            smart = smartapi.GenerateToken().status;
        }
        public async Task<OutputBaseClass> PlaceOrder(OrderInfo? orderInfo)
        {

            if(session && smart)
            {
                OutputBaseClass response = await smartapi.placeOrder(orderInfo).ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass{status = false};
        }
        public async Task<OutputBaseClass> GetLTPData(LTPDataRequest? LTPData)
        {

            if (session && smart)
            {
                OutputBaseClass response = await smartapi.GetLTPData(LTPData).ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass { status = false };
        }
        public async Task<OutputBaseClass> GetTradeBook()
        {

            if (session && smart)
            {
                OutputBaseClass response = await smartapi.getTradeBook().ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass { status = false };
        }
        public async Task<OutputBaseClass> GetOrderBook()
        {

            if (session && smart)
            {
                OutputBaseClass response = await smartapi.getOrderBook().ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass { status = false };
        }
        public async Task<OutputBaseClass> CancelOrder(OrderInfo? orderInfo)
        {

            if (session && smart)
            {
                OutputBaseClass response = await smartapi.cancelOrder(orderInfo).ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass { status = false };
        }

        public async Task<OutputBaseClass> OpenPosition()
        {

            if (session && smart)
            {
                OutputBaseClass response = await smartapi.getPosition().ConfigureAwait(false);
                return response;
            }
            return new OutputBaseClass { status = false };
        }
        public bool UpdateCollection(dynamic rules)
        {
            rules.FirstOrDefault().isProcessed = true;
            return true;
        }
    }
   

}
