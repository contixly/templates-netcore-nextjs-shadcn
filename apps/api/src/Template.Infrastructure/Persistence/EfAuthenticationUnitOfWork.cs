using Microsoft.EntityFrameworkCore;
using Template.Application.Authentication.Ports;

namespace Template.Infrastructure.Persistence;

internal sealed class EfAuthenticationUnitOfWork(AuthDbContext db)
    : IAuthenticationUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the callback failure when rollback cannot complete.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            throw;
        }
    }
}
