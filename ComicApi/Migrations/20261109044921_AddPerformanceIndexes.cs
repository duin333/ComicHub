using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tạo chỉ mục mới TRƯỚC, rồi mới xóa chỉ mục cũ. MySQL không cho xóa chỉ mục mà
            // khóa ngoại đang dựa vào (lỗi 1553), nên thứ tự EF sinh ra mặc định sẽ thất bại
            migrationBuilder.CreateIndex(
                name: "IX_ratings_ComicId_Stars",
                table: "ratings",
                columns: new[] { "ComicId", "Stars" });

            migrationBuilder.DropIndex(
                name: "IX_ratings_ComicId",
                table: "ratings");

            migrationBuilder.CreateIndex(
                name: "IX_comments_CreatedAt",
                table: "comments",
                column: "CreatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_comments_CreatedAt",
                table: "comments");

            // Hoàn tác cũng phải tạo chỉ mục cũ trước rồi mới xóa chỉ mục mới
            migrationBuilder.CreateIndex(
                name: "IX_ratings_ComicId",
                table: "ratings",
                column: "ComicId");

            migrationBuilder.DropIndex(
                name: "IX_ratings_ComicId_Stars",
                table: "ratings");
        }
    }
}
