using ComicApi.Data;
using ComicApi.DTOs.Admin;
using ComicApi.DTOs.Comics;
using ComicApi.Models;
using ComicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ComicApi.Infrastructure;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
[ServiceFilter(typeof(InvalidateCatalogCacheFilter))]
public class AdminChaptersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _files;

    public AdminChaptersController(AppDbContext db, IFileStorage files)
    {
        _db = db;
        _files = files;
    }

    // Danh sách chương của 1 truyện, KỂ CẢ chương đang ẩn
    [HttpGet("comics/{comicId:int}/chapters")]
    public async Task<IActionResult> GetByComic(int comicId, [FromQuery] bool desc = false)
    {
        if (!await _db.Comics.AnyAsync(c => c.Id == comicId))
            return NotFound(new { message = "Không tìm thấy truyện" });

        var q = _db.Chapters.AsNoTracking().Where(c => c.ComicId == comicId);
        q = desc ? q.OrderByDescending(c => c.Number) : q.OrderBy(c => c.Number);

        var list = await q.Select(c => new AdminChapterListItemDto
        {
            Id = c.Id,
            Number = c.Number,
            Title = c.Title,
            IsHidden = c.IsHidden,
            ViewCount = c.ViewCount,
            ImageCount = c.Images.Count,
            CreatedAt = c.CreatedAt
        }).ToListAsync();

        return Ok(list);
    }

    // Chi tiết 1 chương (kể cả đang ẩn) để đổ vào form sửa
    [HttpGet("chapters/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new AdminChapterDetailDto
            {
                Id = c.Id,
                ComicId = c.ComicId,
                Number = c.Number,
                Title = c.Title,
                IsHidden = c.IsHidden,
                ViewCount = c.ViewCount,
                Images = c.Images.OrderBy(i => i.SortOrder)
                    .Select(i => new ChapterImageDto
                    {
                        Id = i.Id,
                        ImageUrl = i.ImageUrl,
                        SortOrder = i.SortOrder
                    }).ToList()
            })
            .FirstOrDefaultAsync();

        return chapter is null
            ? NotFound(new { message = "Không tìm thấy chương" })
            : Ok(chapter);
    }

    [HttpPost("comics/{comicId:int}/chapters")]
    public async Task<IActionResult> Create(int comicId, ChapterSaveDto dto)
    {
        var comic = await _db.Comics.FirstOrDefaultAsync(c => c.Id == comicId);
        if (comic is null) return NotFound(new { message = "Không tìm thấy truyện" });

        if (dto.ImageUrls is null || dto.ImageUrls.Count == 0)
            return BadRequest(new { message = "Chương cần ít nhất 1 ảnh" });
        if (dto.ImageUrls.Any(u => !_files.IsAllowedUrl(u)))
            return BadRequest(new { message = "Có đường dẫn ảnh không hợp lệ" });

        if (await _db.Chapters.AnyAsync(c => c.ComicId == comicId && c.Number == dto.Number))
            return Conflict(new { message = $"Chương số {dto.Number} đã tồn tại" });

        var chapter = new Chapter
        {
            ComicId = comicId,
            Number = dto.Number,
            Title = dto.Title.Trim(),
            IsHidden = dto.IsHidden
        };

        for (int i = 0; i < dto.ImageUrls.Count; i++)
            chapter.Images.Add(new ChapterImage { ImageUrl = dto.ImageUrls[i], SortOrder = i + 1 });

        _db.Chapters.Add(chapter);

        if (!chapter.IsHidden) comic.UpdatedAt = DateTime.UtcNow;   // để truyện nổi lên "mới cập nhật"

        await _db.SaveChangesAsync();

        return Created($"/api/Chapters/{chapter.Id}", new { chapter.Id });
    }

    [HttpPut("chapters/{id:int}")]
    public async Task<IActionResult> Update(int id, ChapterSaveDto dto)
    {
        var chapter = await _db.Chapters.Include(c => c.Images).FirstOrDefaultAsync(c => c.Id == id);
        if (chapter is null) return NotFound(new { message = "Không tìm thấy chương" });

        if (await _db.Chapters.AnyAsync(c =>
                c.ComicId == chapter.ComicId && c.Number == dto.Number && c.Id != id))
            return Conflict(new { message = $"Chương số {dto.Number} đã tồn tại" });

        List<string> removedUrls = new();

        if (dto.ImageUrls is not null)
        {
            if (dto.ImageUrls.Count == 0)
                return BadRequest(new { message = "Chương cần ít nhất 1 ảnh" });
            if (dto.ImageUrls.Any(u => !_files.IsAllowedUrl(u)))
                return BadRequest(new { message = "Có đường dẫn ảnh không hợp lệ" });

            var oldImages = chapter.Images.ToList();
            removedUrls = oldImages.Select(i => i.ImageUrl).Except(dto.ImageUrls).ToList();

            _db.ChapterImages.RemoveRange(oldImages);
            for (int i = 0; i < dto.ImageUrls.Count; i++)
                _db.ChapterImages.Add(new ChapterImage
                {
                    ChapterId = chapter.Id,
                    ImageUrl = dto.ImageUrls[i],
                    SortOrder = i + 1
                });
        }

        chapter.Number = dto.Number;
        chapter.Title = dto.Title.Trim();
        chapter.IsHidden = dto.IsHidden;

        await _db.SaveChangesAsync();

        foreach (var url in removedUrls) _files.DeleteLocal(url);

        return NoContent();
    }

    // Ẩn/hiện nhanh bằng một cú bấm
    [HttpPatch("chapters/{id:int}/toggle-hidden")]
    public async Task<IActionResult> ToggleHidden(int id)
    {
        var chapter = await _db.Chapters.FindAsync(id);
        if (chapter is null) return NotFound(new { message = "Không tìm thấy chương" });

        chapter.IsHidden = !chapter.IsHidden;
        await _db.SaveChangesAsync();

        return Ok(new { chapter.Id, chapter.IsHidden });
    }

    [HttpDelete("chapters/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var chapter = await _db.Chapters.Include(c => c.Images).FirstOrDefaultAsync(c => c.Id == id);
        if (chapter is null) return NotFound(new { message = "Không tìm thấy chương" });

        var urls = chapter.Images.Select(i => i.ImageUrl).ToList();

        _db.Chapters.Remove(chapter);
        await _db.SaveChangesAsync();

        foreach (var url in urls) _files.DeleteLocal(url);

        return NoContent();
    }
}