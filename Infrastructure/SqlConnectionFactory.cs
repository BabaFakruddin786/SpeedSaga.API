using Microsoft.Data.SqlClient;

namespace SpeedSaga.API.Infrastructure;

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SpeedSagaDB")
            ?? throw new InvalidOperationException("Connection string 'SpeedSagaDB' is not configured.");
    }

    public SqlConnection CreateConnection()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString)
        {
            MinPoolSize = 10,
            MaxPoolSize = 300,
            ConnectTimeout = 15,
            MultipleActiveResultSets = true
        };
        return new SqlConnection(builder.ConnectionString);
    }
}
