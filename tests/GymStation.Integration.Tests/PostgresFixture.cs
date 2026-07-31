using GymStation.Infrastructure;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GymStation.Integration.Tests;

/// <summary>
/// One Postgres container per test run; schema applied from the committed migrations
/// so the tests also validate that the migrations actually build the model.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public GymStationDbContext CreateContext(TenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<GymStationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new GymStationDbContext(options, tenant ?? new TenantContext());
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
