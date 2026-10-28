using System.Data.Common;

namespace HrmsAi.Abstractions;

public interface IDbConnectionFactory
{
    DbConnection Create();
}
