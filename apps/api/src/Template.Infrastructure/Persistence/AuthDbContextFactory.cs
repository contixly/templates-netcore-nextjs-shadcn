using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Template.Infrastructure.Persistence;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ??
            "Host=127.0.0.1;Database=template_design";
        var options = new DbContextOptionsBuilder<AuthDbContext>();
        AuthDbContext.Configure(options, connectionString);
        return new AuthDbContext(options.Options);
    }
}
