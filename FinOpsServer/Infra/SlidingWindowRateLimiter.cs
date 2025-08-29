using System.Collections.Concurrent;

namespace FinOpsServer.Infra;

public sealed class SlidingWindowRateLimiter
{
    private readonly int _max;
    private readonly TimeSpan _win;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _buckets = new();  // per-key queues of timestamps

    public SlidingWindowRateLimiter(int maxRequests, TimeSpan window)
    { _max = maxRequests; _win = window; }

    public bool Allow(string key, DateTime nowUtc)
    {
        var q = _buckets.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (q)
        {
            while (q.Count > 0 && nowUtc - q.Peek() >= _win) q.Dequeue();
            if (q.Count >= _max) return false;
            q.Enqueue(nowUtc);
            return true;
        }
    }
}
