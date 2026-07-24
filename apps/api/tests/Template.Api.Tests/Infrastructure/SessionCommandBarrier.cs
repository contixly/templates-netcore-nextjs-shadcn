using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Template.Api.Tests.Infrastructure;

internal sealed class SessionCommandBarrier : DbCommandInterceptor
{
    private CoordinationMode _mode;
    private int _participants;
    private int _arrived;
    private TaskCompletionSource _allBlocked = NewSignal();
    private TaskCompletionSource _release = NewSignal();
    private TaskCompletionSource _updateReady = NewSignal();
    private TaskCompletionSource _deleteCompleted = NewSignal();

    internal void CoordinateParallelSessionDeletes(int participants)
    {
        Reset(CoordinationMode.ParallelDeletes);
        _participants = participants;
    }

    internal void CoordinateSessionDeleteBeforeUpdate() =>
        Reset(CoordinationMode.DeleteBeforeUpdate);

    internal async Task ReleaseParallelCommandsAsync(
        Task operations,
        CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(_allBlocked.Task, operations)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Same(_allBlocked.Task, completed);
        _release.TrySetResult();
        await operations;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsSessionDelete(command))
        {
            if (_mode == CoordinationMode.ParallelDeletes)
            {
                if (Interlocked.Increment(ref _arrived) == _participants)
                {
                    _allBlocked.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }
            else if (_mode == CoordinationMode.DeleteBeforeUpdate)
            {
                await _updateReady.Task.WaitAsync(cancellationToken);
            }
        }
        else if (_mode == CoordinationMode.DeleteBeforeUpdate &&
            IsSessionUpdate(command))
        {
            _updateReady.TrySetResult();
            await _deleteCompleted.Task.WaitAsync(cancellationToken);
        }

        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_mode == CoordinationMode.DeleteBeforeUpdate &&
            IsSessionDelete(command))
        {
            _deleteCompleted.TrySetResult();
        }

        return ValueTask.FromResult(result);
    }

    private void Reset(CoordinationMode mode)
    {
        _mode = mode;
        _participants = 0;
        _arrived = 0;
        _allBlocked = NewSignal();
        _release = NewSignal();
        _updateReady = NewSignal();
        _deleteCompleted = NewSignal();
    }

    private static bool IsSessionDelete(DbCommand command) =>
        command.CommandText.Contains(
            "DELETE FROM auth.sessions",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSessionUpdate(DbCommand command) =>
        command.CommandText.Contains(
            "UPDATE auth.sessions",
            StringComparison.OrdinalIgnoreCase);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum CoordinationMode
    {
        None,
        ParallelDeletes,
        DeleteBeforeUpdate
    }
}
