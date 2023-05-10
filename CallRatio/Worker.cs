using CallRatio.Interface;
using RulesEngineService.Models.Response;
using System.Data;
using System.Data.SqlClient;

namespace CallRatio
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        public Worker(ILogger<Worker> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var processRuleService = scope.ServiceProvider.GetRequiredService<IProcessJob>();
            if (ExceptionalDays())
            {
                //Thread entryThread = new Thread(() => processRuleService.ProcessStart());

                //entryThread.Start();
                processRuleService.ProcessStart();
            }


            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            //    await Task.Delay(1000, stoppingToken);
            //}
        }
        public bool ExceptionalDays()
        {
            DateTime current = DateTime.Now;
            DayOfWeek dayOfWeek = current.DayOfWeek;
            TimeSpan timestart = new TimeSpan(09, 15, 00);
            TimeSpan timeend = new TimeSpan(15, 30, 00);
            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DbConnection")))
            {
                conn.Open();
                SqlCommand command = new SqlCommand();
                command.Connection = conn;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = "common.holiday";
                SqlDataAdapter adapter = new SqlDataAdapter(command);
                DataTable dataTable = new DataTable();
                List<HolidayResp> getHolidays = new List<HolidayResp>();
                adapter.Fill(dataTable);
                if (dataTable.Rows.Count > 0)
                {
                    getHolidays = (from DataRow dr in dataTable.Rows
                                   select new HolidayResp()
                                   {
                                       Id = Convert.ToInt64(dr["Id"]),
                                       Date = Convert.ToDateTime(dr["Date"]),
                                       IsActive = Convert.ToBoolean(dr["IsActive"])
                                   }).ToList();
                }
                conn.Close();
                if (getHolidays.Where(x => Convert.ToDateTime(x.Date).Date == current.Date).Count() == 0 && dayOfWeek != DayOfWeek.Sunday && dayOfWeek != DayOfWeek.Saturday && timestart <= current.TimeOfDay && current.TimeOfDay <= timeend)
                {
                    return true;
                }
            }


            return false;
        }
    }
}