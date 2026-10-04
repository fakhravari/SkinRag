using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Telemetry;
using SkinRag.Api.Domain.Catalog;
using SkinRag.Api.Infrastructure.Persistence.Entities;

namespace SkinRag.Api.Infrastructure.Persistence;

public sealed class SkinRagDbContext(DbContextOptions<SkinRagDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<CatalogPhrase> CatalogPhrases => Set<CatalogPhrase>();

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<CatalogProfile> CatalogProfiles => Set<CatalogProfile>();

    public DbSet<Concern> Concerns => Set<Concern>();

    public DbSet<Ingredient> Ingredients => Set<Ingredient>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductEmbedding> ProductEmbeddings => Set<ProductEmbedding>();

    public DbSet<ConsultationPerformanceLog> ConsultationPerformanceLogs => Set<ConsultationPerformanceLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.Brand).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SkinTypes).HasMaxLength(300).IsRequired();
            entity.Property(x => x.HairTypes).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Concerns).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.Property(x => x.Ingredients).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.Warnings).IsRequired();
            entity.Property(x => x.UsageInstructions).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.SearchKeywords).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Image).IsRequired();
            entity.Property(x => x.FragranceFree).HasDefaultValue(false).IsRequired();
            entity.Property(x => x.FragranceFreeKnown).HasDefaultValue(false).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("IRR");
            entity.Property(x => x.Image).HasColumnType("nvarchar(max)");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(x => x.CategoryDetails).WithMany().HasForeignKey(x => x.IdCategory).IsRequired()
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.BrandDetails).WithMany().HasForeignKey(x => x.IdBrand).IsRequired()
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Variants).WithOne().HasForeignKey(x => x.IdProduct).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductProfiles).WithOne().HasForeignKey(x => x.IdProduct)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductConcerns).WithOne().HasForeignKey(x => x.IdProduct)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.ProductIngredients).WithOne().HasForeignKey(x => x.IdProduct)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Category>().ToTable("Categories");
        modelBuilder.Entity<Category>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.IdParent)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<CatalogPhrase>(entity =>
        {
            entity.ToTable("CatalogPhrases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Phrase).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SearchTerms).HasMaxLength(1000);
            entity.HasIndex(x => x.Phrase);
            entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.IdCategory)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Brand>().ToTable("Brands");
        modelBuilder.Entity<CatalogProfile>().ToTable("CatalogProfiles");
        modelBuilder.Entity<Concern>().ToTable("Concerns");
        modelBuilder.Entity<Ingredient>().ToTable("Ingredients");
        modelBuilder.Entity<ProductProfile>().ToTable("ProductProfiles").HasKey(x => new { x.IdProduct, x.IdCatalogProfile });
        modelBuilder.Entity<ProductProfile>().HasOne(x => x.CatalogProfile).WithMany().HasForeignKey(x => x.IdCatalogProfile)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProductConcern>().ToTable("ProductConcerns").HasKey(x => new { x.IdProduct, x.IdConcern });
        modelBuilder.Entity<ProductConcern>().HasOne(x => x.Concern).WithMany().HasForeignKey(x => x.IdConcern)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ProductIngredient>().ToTable("ProductIngredients")
            .HasKey(x => new { x.IdProduct, x.IdIngredient });
        modelBuilder.Entity<ProductIngredient>().HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IdIngredient)
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
            entity.ToTable("ProductEmbeddings").HasKey(x => new { x.IdProduct, x.Model });
            entity.Property(x => x.Model).HasMaxLength(100);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.IdProduct).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ConsultationPerformanceLog>(entity =>
        {
            entity.ToTable("ConsultationPerformanceLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StartedAtLocal).HasColumnType("datetime2(3)");
            entity.Property(x => x.CompletedAtLocal).HasColumnType("datetime2(3)");
            entity.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ErrorType).HasMaxLength(200);
            entity.Property(x => x.Intent).HasMaxLength(40);
            entity.Property(x => x.RetrievalMethod).HasMaxLength(60);
            entity.Property(x => x.ResponseMode).HasMaxLength(30);
            entity.Property(x => x.QuerySource).HasMaxLength(500);
            entity.Property(x => x.SearchQuery).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ModelCallsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ClientIpAddress).HasMaxLength(45);
            entity.Property(x => x.UserAgent).HasMaxLength(1000);
            entity.Property(x => x.BrowserName).HasMaxLength(40);
            entity.Property(x => x.OperatingSystem).HasMaxLength(40);
            entity.Property(x => x.RequestPath).HasMaxLength(512);
            entity.Property(x => x.HttpMethod).HasMaxLength(10);
            entity.Property(x => x.TraceIdentifier).HasMaxLength(64);
            entity.HasIndex(x => x.StartedAtLocal);
        });
    }
}
