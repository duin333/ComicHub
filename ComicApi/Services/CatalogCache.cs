using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ComicApi.Services;

// Mục "Cache" trong appsettings.json
public class CatalogCacheOptions
{
    public int Seconds { get; set; } = 60;        // dữ liệu cache sống tối đa bao lâu; 0 = tắt cache (luôn đọc database)
    public int MaxEntries { get; set; } = 500;    // tối đa bao nhiêu mục, để cache không phình mãi
}

public record CatalogCacheStats(long Hits, long Misses, long Invalidations, long Entries);

public interface ICatalogCache
{
    // Có trong cache thì trả ngay, chưa có thì gọi factory (đọc database) một lần rồi lưu lại
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory);

    // Xóa toàn bộ cache. Gọi khi admin sửa truyện, chương, thể loại
    void Invalidate();

    CatalogCacheStats Stats { get; }
}

// Cache trong bộ nhớ cho dữ liệu công khai của trang chủ và danh sách truyện (không bao giờ dùng cho dữ liệu riêng của từng người)
public sealed class CatalogCache : ICatalogCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly TimeSpan _ttl;
    private CancellationTokenSource _version = new();      // hủy "phiên bản" này = mọi mục đang lưu đều hết hạn
    private long _hits, _misses, _invalidations;

    public CatalogCache(IOptions<CatalogCacheOptions> options)
    {
        var o = options.Value;
        _ttl = TimeSpan.FromSeconds(Math.Max(0, o.Seconds));
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, o.MaxEntries), TrackStatistics = true });
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        if (_ttl == TimeSpan.Zero) return await factory();            // cache đang tắt

        if (_cache.TryGetValue(key, out T? hit))
        {
            Interlocked.Increment(ref _hits);
            return hit!;
        }

        // Nhiều người cùng vào lúc cache trống: chỉ MỘT người đi đọc database, những người còn lại chờ rồi dùng kết quả đó
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out hit))
            {
                Interlocked.Increment(ref _hits);
                return hit!;
            }

            Interlocked.Increment(ref _misses);

            // Lấy "phiên bản" TRƯỚC khi đọc database. Nếu admin sửa dữ liệu (Invalidate) trong lúc đang đọc,
            // phiên bản này đã bị hủy nên kết quả cũ vừa đọc xong sẽ bị bỏ ngay, không bị lưu nhầm
            var token = Volatile.Read(ref _version).Token;
            var value = await factory();

            _cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = _ttl }
                .AddExpirationToken(new CancellationChangeToken(token)));

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Exchange(ref _version, new CancellationTokenSource()).Cancel();
        Interlocked.Increment(ref _invalidations);
    }

    public CatalogCacheStats Stats => new(
        Interlocked.Read(ref _hits), Interlocked.Read(ref _misses), Interlocked.Read(ref _invalidations),
        _cache.GetCurrentStatistics()?.CurrentEntryCount ?? 0);

    public void Dispose() => _cache.Dispose();
}