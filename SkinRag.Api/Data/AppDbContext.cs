using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Models;

namespace SkinRag.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Concern> Concerns => Set<Concern>();

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductEmbedding> ProductEmbeddings => Set<ProductEmbedding>();

    public DbSet<ConsultationPerformanceLog> ConsultationPerformanceLogs => Set<ConsultationPerformanceLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.Brand).HasMaxLength(100);
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.SkinTypes).HasMaxLength(300);
            entity.Property(x => x.HairTypes).HasMaxLength(300);
            entity.Property(x => x.Concerns).HasMaxLength(500);
            entity.Property(x => x.Sku).HasMaxLength(80);
            entity.HasIndex(x => x.Sku).IsUnique().HasFilter("[Sku] IS NOT NULL");
            entity.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("IRR");
            entity.Property(x => x.UsageInstructions).HasMaxLength(2000);
            entity.Property(x => x.SearchKeywords).HasMaxLength(1000);
            entity.Property(x => x.Image).HasColumnType("nvarchar(max)");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(x => x.CategoryDetails).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.BrandDetails).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Variants).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductProfiles).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductConcerns).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductIngredients).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Category>().ToTable("Categories");
        modelBuilder.Entity<Category>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<Brand>().ToTable("Brands");
        modelBuilder.Entity<Profile>().ToTable("Profiles");
        modelBuilder.Entity<Concern>().ToTable("Concerns");
        modelBuilder.Entity<Ingredient>().ToTable("Ingredients");
        modelBuilder.Entity<ProductProfile>().ToTable("ProductProfiles").HasKey(x => new
        {
            x.ProductId,
            x.ProfileId
        });
        modelBuilder.Entity<ProductProfile>().HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProductConcern>().ToTable("ProductConcerns").HasKey(x => new
        {
            x.ProductId,
            x.ConcernId
        });
        modelBuilder.Entity<ProductConcern>().HasOne(x => x.Concern).WithMany().HasForeignKey(x => x.ConcernId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProductIngredient>().ToTable("ProductIngredients").HasKey(x => new
        {
            x.ProductId,
            x.IngredientId
        });
        modelBuilder.Entity<ProductIngredient>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants");
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.SizeValue).HasPrecision(10, 2);
            entity.Property(x => x.Sku).HasMaxLength(100);
            entity.HasIndex(x => x.Sku).IsUnique();
        });
        modelBuilder.Entity<ProductEmbedding>(entity =>
        {
            entity.ToTable("ProductEmbeddings").HasKey(x => new
            {
                x.ProductId,
                x.Model
            });
            entity.Property(x => x.Model).HasMaxLength(100);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ConsultationPerformanceLog>(entity =>
        {
            entity.ToTable("ConsultationPerformanceLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ErrorType).HasMaxLength(200);
            entity.Property(x => x.Intent).HasMaxLength(40);
            entity.Property(x => x.RetrievalMethod).HasMaxLength(60);
            entity.Property(x => x.ResponseMode).HasMaxLength(30);
            entity.HasIndex(x => x.StartedAtUtc);
        });
    }
}
