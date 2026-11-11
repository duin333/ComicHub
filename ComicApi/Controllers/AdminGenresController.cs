using ComicApi.Data;
using ComicApi.DTOs.Admin;
using ComicApi.DTOs.Comics;
using ComicApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ComicApi.Infrastructure;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/admin/genres")]
[Authorize(Roles = Roles.Admin)]
[ServiceFilter(typeof(InvalidateCatalogCacheFilter))]
public class AdminGenresController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminGenresController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Create(GenreSaveDto dto)
    {
        var name = dto.Name.Trim();
        if (await _db.Genres.AnyAsync(g => g.Name == name))
            return Conflict(new { message = "Thể loại đã tồn tại" });

        var genre = new Genre { Name = name };
        _db.Genres.Add(genre);
        await _db.SaveChangesAsync();

        return Created("/api/Genres", new GenreDto { Id = genre.Id, Name = genre.Name });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GenreSaveDto dto)
    {
        var genre = await _db.Genres.FindAsync(id);
        if (genre is null) return NotFound(new { message = "Không tìm thấy thể loại" });

        var name = dto.Name.Trim();
        if (await _db.Genres.AnyAsync(g => g.Name == name && g.Id != id))
            return Conflict(new { message = "Thể loại đã tồn tại" });

        genre.Name = name;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var genre = await _db.Genres.FindAsync(id);
        if (genre is null) return NotFound(new { message = "Không tìm thấy thể loại" });

        var used = await _db.ComicGenres.CountAsync(cg => cg.GenreId == id);
        if (used > 0)
            return Conflict(new { message = $"Thể loại đang được dùng bởi {used} truyện, hãy gỡ khỏi truyện trước" });

        _db.Genres.Remove(genre);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}