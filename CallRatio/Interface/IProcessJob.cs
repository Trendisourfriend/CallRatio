using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CallRatio.Interface
{
    public interface IProcessJob
    {
        public Task<Boolean> ProcessStart();
    }
}
