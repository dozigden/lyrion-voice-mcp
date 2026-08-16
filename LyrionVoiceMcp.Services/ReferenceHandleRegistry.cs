using System.Security.Cryptography;

namespace LyrionVoiceMcp.Services;

internal sealed class ReferenceHandleRegistry
{
    internal static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);
    internal const int DefaultCapacity = 10_000;
    private const int RandomByteCount = 8;
    private const int EncodedKeyLength = RandomByteCount * 2;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Queue<Entry> issuanceOrder = new();
    private readonly object sync = new();
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan lifetime;
    private readonly int capacity;

    public ReferenceHandleRegistry(TimeProvider timeProvider)
        : this(timeProvider, DefaultLifetime, DefaultCapacity)
    {
    }

    internal ReferenceHandleRegistry(
        TimeProvider timeProvider,
        TimeSpan lifetime,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "The reference lifetime must be positive.");
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "The reference capacity must be positive.");
        }

        this.timeProvider = timeProvider;
        this.lifetime = lifetime;
        this.capacity = capacity;
    }

    public string Issue<TValue>(string prefix, TValue value)
        where TValue : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(value);

        lock (sync)
        {
            var now = timeProvider.GetUtcNow();
            RemoveExpired(now);
            while (entries.Count >= capacity)
            {
                RemoveOldest();
            }

            string handle;
            do
            {
                handle = prefix + CreateRandomKey();
            }
            while (entries.ContainsKey(handle));

            var entry = new Entry(handle, value, now.Add(lifetime));
            entries.Add(handle, entry);
            issuanceOrder.Enqueue(entry);
            return handle;
        }
    }

    public TValue? Resolve<TValue>(string prefix, string reference)
        where TValue : class
    {
        if (string.IsNullOrEmpty(reference)
            || reference.Length != prefix.Length + EncodedKeyLength
            || !reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        lock (sync)
        {
            var now = timeProvider.GetUtcNow();
            RemoveExpired(now);
            if (!entries.TryGetValue(reference, out var entry)
                || entry.Value is not TValue value)
            {
                return null;
            }

            if (entry.ExpiresAt <= now)
            {
                RemoveIfCurrent(entry);
                return null;
            }

            return value;
        }
    }

    internal int Count
    {
        get
        {
            lock (sync)
            {
                RemoveExpired(timeProvider.GetUtcNow());
                return entries.Count;
            }
        }
    }

    private static string CreateRandomKey()
    {
        Span<byte> bytes = stackalloc byte[RandomByteCount];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (issuanceOrder.TryPeek(out var entry)
            && entry.ExpiresAt <= now)
        {
            issuanceOrder.Dequeue();
            RemoveIfCurrent(entry);
        }
    }

    private void RemoveOldest()
    {
        while (issuanceOrder.TryDequeue(out var entry))
        {
            if (RemoveIfCurrent(entry))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "The reference registry could not evict its oldest entry.");
    }

    private bool RemoveIfCurrent(Entry entry)
    {
        if (!entries.TryGetValue(entry.Handle, out var current)
            || !ReferenceEquals(current, entry))
        {
            return false;
        }

        return entries.Remove(entry.Handle);
    }

    private sealed record Entry(
        string Handle,
        object Value,
        DateTimeOffset ExpiresAt);
}
