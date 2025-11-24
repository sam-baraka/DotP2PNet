using System.Collections.Concurrent;

namespace Dotp2pNet.Core.Concurrency;

/// <summary>
/// Helper utilities for managing concurrency and async operations.
/// </summary>
public static class AsyncHelper
{
    /// <summary>
    /// Creates a keyed semaphore for coordinating access to resources by key.
    /// Useful for ensuring only one operation per piece/peer/resource at a time.
    /// </summary>
    public class KeyedSemaphore<TKey> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _semaphores = new();
        private readonly int _maxCount;

        public KeyedSemaphore(int maxCount = 1)
        {
            _maxCount = maxCount;
        }

        public async Task<IDisposable> LockAsync(TKey key, CancellationToken ct = default)
        {
            var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_maxCount, _maxCount));
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            return new SemaphoreReleaser(semaphore, key, _semaphores);
        }

        private class SemaphoreReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly TKey _key;
            private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _semaphores;
            private bool _disposed;

            public SemaphoreReleaser(
                SemaphoreSlim semaphore,
                TKey key,
                ConcurrentDictionary<TKey, SemaphoreSlim> semaphores)
            {
                _semaphore = semaphore;
                _key = key;
                _semaphores = semaphores;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                _semaphore.Release();

                // Clean up if no one is waiting
                if (_semaphore.CurrentCount == 1)
                {
                    _semaphores.TryRemove(_key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Executes an async operation with a timeout.
    /// </summary>
    public static async Task<T> WithTimeout<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
        }
    }

    /// <summary>
    /// Executes an async operation with a timeout (non-generic version).
    /// </summary>
    public static async Task WithTimeout(
        Task task,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
        }
    }
}
