// <copyright file="ReadyStartupTaskRunner.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Serilog;

namespace PokeSoulLinkBot.Bot;

/// <summary>
/// Starts expensive ready-time initialization without blocking the Discord gateway task.
/// </summary>
public sealed class ReadyStartupTaskRunner
{
    private readonly object synchronizationLock = new ();
    private readonly Func<Task> startupTaskFactory;
    private Task? currentStartupTask;
    private bool completedSuccessfully;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadyStartupTaskRunner"/> class.
    /// </summary>
    /// <param name="startupTaskFactory">The startup task to run in the background.</param>
    public ReadyStartupTaskRunner(Func<Task> startupTaskFactory)
    {
        this.startupTaskFactory = startupTaskFactory ?? throw new ArgumentNullException(nameof(startupTaskFactory));
    }

    /// <summary>
    /// Gets the currently running startup task.
    /// </summary>
    public Task? CurrentStartupTask
    {
        get
        {
            lock (this.synchronizationLock)
            {
                return this.currentStartupTask;
            }
        }
    }

    /// <summary>
    /// Starts the configured startup task in the background.
    /// </summary>
    /// <returns>A completed task so the Discord gateway task is not blocked.</returns>
    public Task HandleReadyAsync()
    {
        lock (this.synchronizationLock)
        {
            if (this.completedSuccessfully || this.currentStartupTask is { IsCompleted: false })
            {
                Log.Debug(
                    "Ready startup task skipped. CompletedSuccessfully={CompletedSuccessfully}, IsRunning={IsRunning}.",
                    this.completedSuccessfully,
                    this.currentStartupTask is { IsCompleted: false });
                return Task.CompletedTask;
            }

            Log.Information("Starting ready startup task in the background.");
            this.currentStartupTask = Task.Run(this.RunStartupTaskAsync);
        }

        return Task.CompletedTask;
    }

    private async Task RunStartupTaskAsync()
    {
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            await this.startupTaskFactory();

            lock (this.synchronizationLock)
            {
                this.completedSuccessfully = true;
            }

            Log.Information(
                "Ready startup task completed in {ElapsedMilliseconds} ms.",
                (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Ready startup task failed.");
        }
    }
}
