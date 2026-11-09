using ComicApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ComicApi.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.ToTable("comments");
        b.HasKey(c => c.Id);
        b.Property(c => c.Content).IsRequired().HasMaxLength(1000);

        b.HasOne(c => c.User).WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(c => c.Comic).WithMany(x => x.Comments)
            .HasForeignKey(c => c.ComicId).OnDelete(DeleteBehavior.Cascade);
        b.Property(c => c.GifUrl).HasMaxLength(500);
        // 10.13: phản hồi. Xóa bình luận gốc thì xóa luôn phản hồi (MySQL cho phép cascade tự tham chiếu)
        b.HasOne(c => c.Parent).WithMany(c => c.Replies)
         .HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Cascade);

        // Xóa tài khoản được nhắc tên thì chỉ mất chữ "@tên", phản hồi vẫn còn
        b.HasOne(c => c.ReplyToUser).WithMany()
         .HasForeignKey(c => c.ReplyToUserId).OnDelete(DeleteBehavior.SetNull);

        // Tăng tốc việc lấy bình luận gốc của một truyện (thêm cột ParentId vào chỉ mục)
        b.HasIndex(c => new { c.ComicId, c.ParentId, c.CreatedAt });

        // 12.1: trang admin luôn sắp xếp bình luận mới nhất trước, và thẻ thống kê lọc theo thời gian
        b.HasIndex(c => c.CreatedAt);
    }
}