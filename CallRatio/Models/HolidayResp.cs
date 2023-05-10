using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RulesEngineService.Models.Response
{
    public class HolidayResp
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public bool IsActive { get; set; }
    }
}
