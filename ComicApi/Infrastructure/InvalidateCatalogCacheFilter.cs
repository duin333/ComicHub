using ComicApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ComicApi.Infrastructure;

// Gắn lên controller admin bằng [ServiceFilter(typeof(InvalidateCatalogCacheFilter))].
// Sau mỗi thao tác GHI thành công (POST, PUT, PATCH, DELETE trả mã 2xx) thì xóa cache danh sách truyện,
// nhờ vậy sửa truyện, chương, thể loại xong là trang chủ thấy ngay, không phải chờ cache hết hạn
public class InvalidateCatalogCacheFilter : IActionFilter
{
    private readonly ICatalogCache _cache;

    public InvalidateCatalogCacheFilter(ICatalogCache cache) => _cache = cache;

    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;

        if (context.Exception is not null && !context.ExceptionHandled) return;          // lỗi: dữ liệu chưa đổi

        var status = (context.Result as IStatusCodeActionResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (status is >= 200 and < 300) _cache.Invalidate();
    }
}