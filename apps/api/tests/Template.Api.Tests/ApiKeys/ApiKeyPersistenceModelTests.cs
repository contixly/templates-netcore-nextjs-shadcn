using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Template.Infrastructure.ApiKeys;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyPersistenceModelTests
{
    [Fact]
    public void Api_keys_have_the_required_secure_relational_model()
    {
        using var db = new TemplateDbContext(
            new DbContextOptionsBuilder<TemplateDbContext>()
                .UseNpgsql("Host=localhost;Database=template_api_keys_model")
                .Options);

        Assert.NotNull(db.ApiKeys);

        var entity = db.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ApiKeyEntity))!;

        Assert.Equal("api_keys", entity.GetTableName());
        Assert.Equal("auth", entity.GetSchema());
        Assert.Equal(typeof(Guid), entity.FindProperty(nameof(ApiKeyEntity.Id))!.ClrType);
        Assert.Equal(typeof(byte[]), entity.FindProperty(nameof(ApiKeyEntity.KeyHash))!.ClrType);
        Assert.Equal(typeof(string[]), entity.FindProperty(nameof(ApiKeyEntity.Scopes))!.ClrType);
        Assert.Null(typeof(ApiKeyEntity).GetProperty("Key"));
        Assert.Null(typeof(ApiKeyEntity).GetProperty("Secret"));

        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "num_nonnulls", "user_id", "organization_id", "= 1"));
        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "octet_length(key_hash)", "32"));
        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "cardinality(scopes)", "basic:read", "organization:read", "teamMember:read"));
        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "rate_limit_window_seconds", "60", "3600", "86400"));
        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "rate_limit_max", "1", "1000000"));
        Assert.Contains(entity.GetCheckConstraints(), check =>
            ContainsAll(check.Sql, "request_count", ">= 0"));

        var foreignKeys = entity.GetForeignKeys().ToArray();
        Assert.Contains(foreignKeys, foreignKey =>
            PropertyNames(foreignKey).SequenceEqual([nameof(ApiKeyEntity.UserId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Contains(foreignKeys, foreignKey =>
            PropertyNames(foreignKey).SequenceEqual([nameof(ApiKeyEntity.OrganizationId)])
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade);

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ApiKeyEntity.KeyHash)]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.GetFilter() == "revoked_at IS NULL"
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(ApiKeyEntity.UserId),
                    nameof(ApiKeyEntity.CreatedAt),
                    nameof(ApiKeyEntity.Id)
                ]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.GetFilter() == "revoked_at IS NULL"
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(ApiKeyEntity.OrganizationId),
                    nameof(ApiKeyEntity.CreatedAt),
                    nameof(ApiKeyEntity.Id)
                ]));
    }

    private static bool ContainsAll(string? value, params string[] fragments) =>
        value is not null && fragments.All(fragment => value.Contains(
            fragment,
            StringComparison.Ordinal));

    private static IReadOnlyList<string> PropertyNames(IForeignKey foreignKey) =>
        foreignKey.Properties.Select(property => property.Name).ToArray();
}
