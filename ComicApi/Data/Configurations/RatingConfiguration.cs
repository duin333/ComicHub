using ComicApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ComicApi.Data.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        // Ràng buộc ở tầng database: số sao chỉ được từ 1 đến 5
        b.ToTable("ratings", t => t.HasCheckConstraint("CK_ratings_stars", "Stars BETWEEN 1 AND 5"));

        b.HasKey(r => new { r.UserId, r.ComicId });      // mỗi người chỉ chấm 1 lần cho 1 truyện

        b.HasOne(r => r.User).WithMany()
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(r => r.Comic).WithMany(c => c.Ratings)
            .HasForeignKey(r => r.ComicId).OnDelete(DeleteBehavior.Cascade);

        // 12.1: chỉ mục phủ cho truy vấn tính trung bình, đếm và phân bố số sao theo truyện.
        // Cũng thay cho chỉ mục khóa ngoại ComicId (tiền tố của chỉ mục này)
        b.HasIndex(r => new { r.ComicId, r.Stars });
    }
}