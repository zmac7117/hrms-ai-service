using System.Data.Common;
using HrmsAi.Abstractions;
using Microsoft.Data.SqlClient;

namespace HrmsAi.Platform.Data;

public class SqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _cs = config.GetConnectionString("HrmsAi")
        ?? throw new InvalidOperationException("Connection string 'HrmsAi' is missing.");

    public DbConnection Create() => new SqlConnection(_cs);
}
