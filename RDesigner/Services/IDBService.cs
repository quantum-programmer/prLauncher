using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using RDesigner.Models;

namespace RDesigner.Services
{
    public interface IDBService
    {           
        public bool CheckConnection();
        public string GetConnectionString();
        public Task<ARMReport?> GetReportById(int id);
        public Task<List<ARMReport>> GetAllReportsAsync();
        public Task<Boolean> InsertReportAsync(ARMReport r);
        public Task<Boolean> UpdateReportAsync(ARMReport r);
        public Task<Boolean> DeleteReportAsync(int reportId);
        public Task ListenForReportsChangeAsync(Action<string> notificationHandler);
    }
}
