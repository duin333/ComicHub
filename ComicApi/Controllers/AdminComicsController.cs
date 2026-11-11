using ComicApi.Data;
using ComicApi.DTOs.Admin;
using ComicApi.Extensions;
using ComicApi.Models;
using ComicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ComicApi.Infrastructure;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/admin/comics")]
[Authorize(Roles = Roles.Admin)]
[ServiceFilter(typeof(InvalidateCatalogCacheFilter))]
public class AdminComicsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _files;

    public AdminComicsController(AppDbContext db, IFileStorage files)
    {
        _db = db;
        _files = files;
    }

    [HttpPost]
    public async Task<IActionResult> Create(ComicSaveDto dto)
    {
        var error = await ValidateAsync(dto);
        if (error is not null) return BadRequest(new { message = error });

        var comic = new Comic
        {
            Title = dto.Title.Trim(),
            Description = dto.Description,
            Author = dto.Author,
            CoverUrl = string.IsNullOrWhiteSpace(dto.CoverUrl) ? null : dto.CoverUrl,
            Status = dto.Status,
            UploaderId = User.GetUserId()
        };

        foreach (var genreId in dto.GenreIds.Distinct())
            comic.ComicGenres.Add(new ComicGenre { GenreId = genreId });

        _db.Comics.Add(comic);
        await _db.SaveChangesAsync();

        return Created($"/api/Comics/{comic.Id}", new { comic.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ComicSaveDto dto)
    {
        var comic = await _db.Comics.Include(c => c.ComicGenres).FirstOrDefaultAsync(c => c.Id == id);
        if (comic is null) return NotFound(new { message = "Không tìm thấy truyện" });

        var error = await ValidateAsync(dto);
        if (error is not null) return BadRequest(new { message = error });

        var oldCover = comic.CoverUrl;

        comic.Title = dto.Title.Trim();
        comic.Description = dto.Description;
        comic.Author = dto.Author;
        comic.CoverUrl = string.IsNullOrWhiteSpace(dto.CoverUrl) ? null : dto.CoverUrl;
        comic.Status = dto.Status;
        comic.UpdatedAt = DateTime.UtcNow;

        // Đồng bộ thể loại: chỉ xóa cái bị bỏ, chỉ thêm cái mới (tránh xóa rồi thêm lại cùng khóa)
        var newIds = dto.GenreIds.Distinct().ToHashSet();
        var currentIds = comic.ComicGenres.Select(cg => cg.GenreId).ToHashSet();

        _db.ComicGenres.RemoveRange(comic.ComicGenres.Where(cg => !newIds.Contains(cg.GenreId)).ToList());
        foreach (var genreId in newIds.Where(g => !currentIds.Contains(g)))
            comic.ComicGenres.Add(new ComicGenre { GenreId = genreId });

        await _db.SaveChangesAsync();

        if (oldCover != comic.CoverUrl) _files.DeleteLocal(oldCover);   // dọn ảnh bìa cũ

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var comic = await _db.Comics
            .Include(c => c.Chapters).ThenInclude(ch => ch.Images)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (comic is null) return NotFound(new { message = "Không tìm thấy truyện" });

        var files = comic.Chapters
            .SelectMany(ch => ch.Images.Select(i => i.ImageUrl))
            .Append(comic.CoverUrl)
            .ToList();

        _db.Comics.Remove(comic);   // chương, ảnh, bình luận, theo dõi... tự xóa theo (cascade)
        await _db.SaveChangesAsync();

        foreach (var f in files) _files.DeleteLocal(f);

        return NoContent();
    }

    private async Task<string?> ValidateAsync(ComicSaveDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.CoverUrl) && !_files.IsAllowedUrl(dto.CoverUrl))
            return "CoverUrl không hợp lệ";

        var ids = dto.GenreIds.Distinct().ToList();
        var found = await _db.Genres.CountAsync(g => ids.Contains(g.Id));
        if (found != ids.Count) return "Có thể loại không tồn tại";

        return null;
    }
}