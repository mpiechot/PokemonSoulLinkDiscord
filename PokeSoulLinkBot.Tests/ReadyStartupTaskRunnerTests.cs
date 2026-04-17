using PokeSoulLinkBot.Bot;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class ReadyStartupTaskRunnerTests
{
    [Fact]
    public async Task HandleReadyAsync_ShouldReturnCompletedTaskWhileStartupContinuesInBackground()
    {
        var startupCanContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ReadyStartupTaskRunner(async () =>
        {
            startupStarted.SetResult();
            await startupCanContinue.Task;
        });

        var readyTask = runner.HandleReadyAsync();

        Assert.True(readyTask.IsCompleted);
        await startupStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(runner.CurrentStartupTask);
        Assert.False(runner.CurrentStartupTask!.IsCompleted);

        startupCanContinue.SetResult();
        await runner.CurrentStartupTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task HandleReadyAsync_ShouldNotStartDuplicateStartupTaskWhileOneIsRunning()
    {
        var startupCanContinue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;
        var runner = new ReadyStartupTaskRunner(async () =>
        {
            Interlocked.Increment(ref startCount);
            startupStarted.SetResult();
            await startupCanContinue.Task;
        });

        await runner.HandleReadyAsync();
        await startupStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await runner.HandleReadyAsync();

        Assert.Equal(1, Volatile.Read(ref startCount));

        startupCanContinue.SetResult();
        await runner.CurrentStartupTask!.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
