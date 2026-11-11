
using ComicApi.DTOs.Comics;
using ComicApi.Models;
using ComicApi.Repositories;
using ComicApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GenresController : ControllerBase
{
    private readonly IRepository<Genre> _genres;
    private readonly ICatalogCache _cache;

    public GenresController(
        IRepository<Genre> genres,
        ICatalogCache cache)
    {
        _genres = genres;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _cache.GetOrCreateAsync("genres", async () =>
        {
            var list = await _genres.GetAllAsync();

            return list.OrderBy(g => g.Name)
                       .Select(g => new GenreDto
                       {
                           Id = g.Id,
                           Name = g.Name
                       })
                       .ToList();
        });

        return Ok(result);
    }
}
