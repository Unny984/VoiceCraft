using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VoiceCraft.Client.Services;
using VoiceCraft.Core;
using VoiceCraft.Core.Interfaces;

namespace VoiceCraft.Client.iOS;

public class NativeBackgroundService : BackgroundService
{
    private readonly ConcurrentDictionary<Type, BackgroundProcessWrapper> _processes = new();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;

    public override event Action<IBackgroundProcess>? OnProcessStarted;
    public override event Action<IBackgroundProcess>? OnProcessStopped;

    public override async Task StartBackgroundProcess<T>(T process, int timeout = 5000)
    {
        var processType = typeof(T);
        if (_processes.ContainsKey(processType))
            throw new InvalidOperationException("A background process of this type has already been queued/started!");

        var wrapper = new BackgroundProcessWrapper(process);
        _processes.TryAdd(processType, wrapper);

        try
        {
            // Start the background process in a new task
            var startTask = Task.Run(() =>
            {
                try
                {
                    wrapper.Status = BackgroundProcessStatus.Started;
                    InvokeProcessStarted(process);
                    
                    // Start the process - iOS has strict background limitations
                    // We need to be careful about background processing
                    process.Start(wrapper.CancellationToken);
                    
                    wrapper.Status = BackgroundProcessStatus.Completed;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    wrapper.Exception = ex;
                    wrapper.Status = BackgroundProcessStatus.Error;
                }
                finally
                {
                    wrapper.Status = BackgroundProcessStatus.Stopped;
                    InvokeProcessStopped(process);
                }
            });

            // Wait for the process to start or timeout
            var startTime = DateTime.UtcNow;
            while (wrapper.Status == BackgroundProcessStatus.Stopped)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds >= timeout)
                {
                    _processes.TryRemove(processType, out _);
                    wrapper.Dispose();
                    throw new Exception("Failed to start background process within timeout!");
                }

                await Task.Delay(10);
            }

            // Store the task for cleanup
            wrapper.Task = startTask;
        }
        catch
        {
            _processes.TryRemove(processType, out _);
            wrapper.Dispose();
            throw;
        }
    }

    public override async Task StopBackgroundProcess<T>()
    {
        var processType = typeof(T);
        if (!_processes.TryRemove(processType, out var wrapper)) 
            return;

        try
        {
            wrapper.Cancel();
            
            // Wait for the process to stop (with timeout)
            if (wrapper.Task != null)
            {
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(wrapper.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Process didn't stop gracefully, but we'll still dispose
                    // On iOS, we can't force kill the process, so we'll just dispose
                }
            }
            
            InvokeProcessStopped(wrapper.Process);
        }
        finally
        {
            wrapper.Dispose();
        }
    }

    public override bool TryGetBackgroundProcess<T>(out T? process) where T : default
    {
        var processType = typeof(T);
        if (_processes.TryGetValue(processType, out var wrapper))
        {
            process = (T?)wrapper.Process;
            return process != null;
        }

        process = default;
        return false;
    }

    private void InvokeProcessStarted(IBackgroundProcess process)
    {
        var handler = OnProcessStarted;
        if (handler == null) return;

        if (_synchronizationContext == null)
            handler(process);
        else
            _synchronizationContext.Post(_ => handler(process), null);
    }

    private void InvokeProcessStopped(IBackgroundProcess process)
    {
        var handler = OnProcessStopped;
        if (handler == null) return;

        if (_synchronizationContext == null)
            handler(process);
        else
            _synchronizationContext.Post(_ => handler(process), null);
    }

    private class BackgroundProcessWrapper : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _disposed;

        public BackgroundProcessWrapper(IBackgroundProcess process)
        {
            Process = process;
            Status = BackgroundProcessStatus.Stopped;
        }

        public IBackgroundProcess Process { get; }
        public BackgroundProcessStatus Status { get; set; }
        public Exception? Exception { get; set; }
        public Task? Task { get; set; }
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public void Cancel()
        {
            if (!_disposed && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            Cancel();
            _cancellationTokenSource.Dispose();
            _disposed = true;
        }
    }
}
