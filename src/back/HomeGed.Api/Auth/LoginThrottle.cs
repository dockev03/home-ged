using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace HomeGed.Api.Auth;

/// <summary>
/// Limitation des tentatives de connexion propre à Home-GED, appliquée avant tout appel à Paperless :
/// au bout de N échecs dans la fenêtre, la clé (IP ou identifiant) est bloquée pendant la même durée.
/// En mémoire : une seule instance tourne, et un redémarrage remet les compteurs à zéro.
/// </summary>
public sealed class LoginThrottle(IOptions<AuthOptions> options, TimeProvider time)
{
    /// <summary>Au-delà, les entrées périmées sont purgées (protège la mémoire d'identifiants inventés).</summary>
    internal const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private TimeSpan Window => TimeSpan.FromMinutes(options.Value.LockoutMinutes);

    internal int Count => _entries.Count;

    public static string IpKey(string? ip) => "ip:" + (ip ?? "inconnue");

    public static string UserKey(string username) => "user:" + username.Trim().ToUpperInvariant();

    /// <summary>Temps restant avant de pouvoir réessayer, ou null si aucune clé n'est bloquée.</summary>
    public TimeSpan? GetRetryAfter(params string[] keys)
    {
        var now = time.GetUtcNow();
        TimeSpan? longest = null;
        foreach (var key in keys)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                lock (entry)
                {
                    if (entry.BlockedUntil is { } until && until > now && (longest is null || until - now > longest))
                    {
                        longest = until - now;
                    }
                }
            }
        }

        return longest;
    }

    public void RecordFailure(params string[] keys)
    {
        var now = time.GetUtcNow();
        foreach (var key in keys)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry { WindowStart = now });
            lock (entry)
            {
                if (now - entry.WindowStart >= Window)
                {
                    entry.WindowStart = now;
                    entry.Failures = 0;
                }

                entry.Failures++;
                if (entry.Failures >= options.Value.MaxFailedAttempts)
                {
                    entry.BlockedUntil = now + Window;
                    entry.WindowStart = now;
                    entry.Failures = 0;
                }
            }
        }

        if (_entries.Count > SweepThreshold)
        {
            Sweep(now);
        }
    }

    public void Reset(params string[] keys)
    {
        foreach (var key in keys)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            lock (entry)
            {
                if (now - entry.WindowStart >= Window && (entry.BlockedUntil is null || entry.BlockedUntil <= now))
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }
    }

    private sealed class Entry
    {
        public DateTimeOffset WindowStart { get; set; }

        public int Failures { get; set; }

        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
