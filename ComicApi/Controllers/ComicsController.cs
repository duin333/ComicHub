using ComicApi.DTOs.Comics;
using ComicApi.Repositories;
using Microsoft.AspNetCore.Mvc;
using ComicApi.Services;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComicsController : ControllerBase
{
    private readonly IComicRepository _comics;
    private readonly IChapterRepository _chapters;
    private readonly ICatalogCache _cache;

    public ComicsController(IComicRepository comics, IChapterRepository chapters, ICatalogCache cache)
    {
        _comics = comics;
        _chapters = chapters;
        _cache = cache;
    }

    private static readonly HashSet<string> CacheableSorts =
    new() { "", "updated", "newest", "views", "title", "rating", "follows" };

    // Chỉ cache các danh sách "duyệt" phổ biến. Tìm theo từ khóa, trang sâu hoặc tham số lạ đi thẳng xuống database,
    // để người lạ không thể gửi vô số biến thể làm cache đầy rác
    private static string? SearchCacheKey(ComicQueryParams q)
    {
        var sort = (q.Sort ?? "").ToLowerInvariant();
        var status = q.Status ?? "";
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 50);

        if (!string.IsNullOrWhiteSpace(q.Keyword)) return null;
        if (!CacheableSorts.Contains(sort)) return null;
        if (status is not ("" or "Ongoing" or "Completed")) return null;
        if (q.GenreId is < 1 or > 99) return null;
        if (page > 5 || size > 24) return null;

        return $"search:{sort}:{q.GenreId}:{status}:{page}:{size}";
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] ComicQueryParams query)
    {
        var key = SearchCacheKey(query);
        if (key is null) return Ok(await _comics.SearchAsync(query));

        return Ok(await _cache.GetOrCreateAsync(key, () => _comics.SearchAsync(query)));
    }

    [HttpGet("hot")]
    public async Task<IActionResult> Hot([FromQuery] int take = 10)
    {
        take = Math.Clamp(take, 1, 50);
        return Ok(await _cache.GetOrCreateAsync($"hot:{take}", async () =>
        {
            var result = await _comics.SearchAsync(new ComicQueryParams { Sort = "views", PageSize = take });
            return result.Items;
        }));
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] int take = 10)
    {
        take = Math.Clamp(take, 1, 50);
        return Ok(await _cache.GetOrCreateAsync($"latest:{take}", async () =>
        {
            var result = await _comics.SearchAsync(new ComicQueryParams { Sort = "updated", PageSize = take });
            return result.Items;
        }));
    }

    [HttpGet("featured")]
    public async Task<IActionResult> Featured([FromQuery] int take = 5)
    {
        take = Math.Clamp(take, 1, 10);
        return Ok(await _cache.GetOrCreateAsync($"featured:{take}", async () =>
        {
            var top = await _comics.SearchAsync(new ComicQueryParams { Sort = "views", PageSize = take });
            var result = new List<ComicDetailDto>();
            foreach (var c in top.Items)
            {
                var detail = await _comics.GetDetailAsync(c.Id);
                if (detail is not null) result.Add(detail);
            }
            return result;
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var comic = await _comics.GetDetailAsync(id);
        return comic is null
            ? NotFound(new { message = "Không tìm thấy truyện" })
            : Ok(comic);
    }

    // GET /api/Comics/1/chapters?desc=true
    [HttpGet("{id:int}/chapters")]
    public async Task<IActionResult> GetChapters(int id, [FromQuery] bool desc = false)
    {
        if (!await _comics.ExistsAsync(id))
            return NotFound(new { message = "Không tìm thấy truyện" });

        return Ok(await _chapters.GetByComicAsync(id, desc));
    }
}