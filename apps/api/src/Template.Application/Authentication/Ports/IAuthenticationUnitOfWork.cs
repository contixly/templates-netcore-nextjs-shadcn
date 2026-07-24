namespace Template.Application.Authentication.Ports;

public interface IAuthenticationUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
