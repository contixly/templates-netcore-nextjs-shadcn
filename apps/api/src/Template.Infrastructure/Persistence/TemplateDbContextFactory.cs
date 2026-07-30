using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Template.Infrastructure.Persistence;

public sealed class TemplateDbContextFactory : IDesignTimeDbContextFactory<TemplateDbContext>
{
    public TemplateDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ??
            "Host=127.0.0.1;Database=template_design";
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, connectionString);
        return new TemplateDbContext(options.Options);
    }
}
