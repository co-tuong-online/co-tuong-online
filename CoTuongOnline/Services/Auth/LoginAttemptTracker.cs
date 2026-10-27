using System.Collections.Concurrent;

namespace CoTuongOnline.Services.Auth;

/// <summary>
/// Khoá tạm tài khoản khi đăng nhập sai liên tiếp (chống dò mật khẩu).
/// Sai 5 lần trong 15 phút → khoá 5 phút. Đăng nhập đúng → xoá bộ đếm.
/// Lưu trong RAM: khởi động lại server thì bộ đếm về 0 (chấp nhận được với đồ án).
/// </summary>
public sealed class LoginAttemptTracker
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public int Failures;
        public DateTime FirstFailureAt;
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>Thời gian còn bị khoá (null nếu không bị khoá).</summary>
    public TimeSpan? LockedFor(string key)
    {
        if (!_entries.TryGetValue(key, out var e)) return null;
        lock (e)
        {
            if (e.LockedUntil is DateTime until && until > DateTime.UtcNow) return until - DateTime.UtcNow;
            return null;
        }
    }

    public void RecordFailure(string key)
    {
        var e = _entries.GetOrAdd(key, _ => new Entry());
        lock (e)
        {
            var now = DateTime.UtcNow;
            bool lockExpired = e.LockedUntil is DateTime until && until <= now;
            if (lockExpired || now - e.FirstFailureAt > FailureWindow)
            {
                e.Failures = 0;
                e.LockedUntil = null;
            }
            if (e.Failures == 0) e.FirstFailureAt = now;
            e.Failures++;
            if (e.Failures >= MaxFailures) e.LockedUntil = now + LockDuration;
        }
    }

    public void Reset(string key) => _entries.TryRemove(key, out _);
}
