using Npgsql;
using Testcontainers.PostgreSql;

namespace Template.Api.Tests.Infrastructure;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18.4")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    public async ValueTask InitializeAsync() =>
        await _container.StartAsync();

    public async Task<(string DatabaseName, string ConnectionString)> CreateDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var databaseName = $"template_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
            IncludeErrorDetail = false
        };
        return (databaseName, builder.ConnectionString);
    }

    public async Task DropDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid()
                """;
            terminate.Parameters.AddWithValue("databaseName", databaseName);
            await terminate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await drop.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() =>
        await _container.DisposeAsync();
}
