using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Application.Abstractions;
using SkinRag.Api.Application.Knowledge;
using SkinRag.Api.Domain.Catalog;
using SkinRag.Api.Infrastructure.Catalog;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Infrastructure.Persistence.Entities;

namespace SkinRag.Api.Infrastructure.Knowledge;

public sealed class KnowledgeIndexService(
    IDbContextFactory<SkinRagDbContext> dbFactory,
    IOllamaClient ollama,
    IConfiguration configuration,
    ILogger<KnowledgeIndexService> logger) : IKnowledgeIndex
{
    private const string DefaultEmbeddingVersion = "catalog-v4";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _isRebuilding, _processed, _total, _cached;
    private string? _lastError;
    private KnowledgeSnapshot _snapshot = new(Array.Empty<KnowledgeDocument>(), false, null);

    public KnowledgeSnapshot Snapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    public KnowledgeIndexStatus Status()
    {
        var snapshot = Snapshot();
        return new KnowledgeIndexStatus(
            snapshot.Documents.Count,
            snapshot.IsReady,
            snapshot.UpdatedAtUtc,
            Volatile.Read(ref _isRebuilding) == 1,
            Volatile.Read(ref _processed),
            Volatile.Read(ref _total),
            Volatile.Read(ref _cached),
            ollama.EmbeddingModel,
            configuration["Rag:EmbeddingVersion"] ?? DefaultEmbeddingVersion,
            configuration["Ollama:ChatModel"] ?? "",
            Volatile.Read(ref _lastError));
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default, bool force = false)
    {
        await _gate.WaitAsync(cancellationToken);
        Volatile.Write(ref _isRebuilding, 1);
        Volatile.Write(ref _processed, 0);
        Volatile.Write(ref _cached, 0);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            var products = await CatalogQueryService
                .Hydrate(db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id))
                .ToListAsync(cancellationToken);
            Volatile.Write(ref _total, products.Count);
            var cache = await db.ProductEmbeddings.Where(e => e.Model == ollama.EmbeddingModel)
                .ToDictionaryAsync(e => e.ProductId, cancellationToken);
            var prefix = configuration["Rag:DocumentPrefix"] ?? "search_document: ";
            var version = configuration["Rag:EmbeddingVersion"] ?? DefaultEmbeddingVersion;
            var next = new Dictionary<int, KnowledgeDocument>();
            var pending = new List<(Product Product, string Content, string Hash)>();
            foreach (var p in products)
            {
                var content = ToKnowledgeText(p);
                var hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(version + "|" + prefix + content)));
                if (!force && cache.TryGetValue(p.Id, out var stored) && stored.ContentHash == hash
                    && TryReadVector(stored, out var vector))
                {
                    next[p.Id] = new KnowledgeDocument(p.Id, content, vector);
                    Interlocked.Increment(ref _processed);
                    Interlocked.Increment(ref _cached);
                }
                else
                {
                    pending.Add((p, content, hash));
                }
            }

            var batchSize = Math.Clamp(configuration.GetValue("Rag:EmbeddingBatchSize", 16), 1, 64);
            foreach (var batch in pending.Chunk(batchSize))
            {
                var embeddings = await ollama.EmbedBatchAsync(batch.Select(x => prefix + x.Content).ToArray(),
                    cancellationToken);
                for (var i = 0; i < batch.Length; i++)
                {
                    var item = batch[i];
                    if (!cache.TryGetValue(item.Product.Id, out var record))
                    {
                        record = new ProductEmbedding
                        {
                            ProductId = item.Product.Id,
                            Model = ollama.EmbeddingModel
                        };
                        db.ProductEmbeddings.Add(record);
                        cache[item.Product.Id] = record;
                    }

                    record.ContentHash = item.Hash;
                    record.Dimensions = embeddings[i].Length;
                    record.VectorJson = JsonSerializer.Serialize(embeddings[i]);
                    record.UpdatedAtUtc = DateTime.UtcNow;
                    next[item.Product.Id] = new KnowledgeDocument(item.Product.Id, item.Content, embeddings[i]);
                }

                await db.SaveChangesAsync(cancellationToken);
                Interlocked.Add(ref _processed, batch.Length);
                logger.LogInformation("Index progress {Processed}/{Total}", _processed, _total);
            }

            if (next.Values.Select(x => x.Embedding.Length).Distinct().Count() > 1)
            {
                throw new InvalidOperationException(
                    "Embedding dimensions changed; force a rebuild with a stable embedding model.");
            }

            Volatile.Write(ref _snapshot,
                new KnowledgeSnapshot(next.Values.OrderBy(x => x.ProductId).ToArray(), true, DateTime.UtcNow));
            Volatile.Write(ref _lastError, null);
            logger.LogInformation("Index ready: {Count} products, {Cached} from SQL cache", next.Count, _cached);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _lastError,
                "بازسازی دانش ناموفق بود؛ اتصال دیتابیس و Ollama و گزارش سرور را بررسی کنید.");
            throw;
        }
        finally
        {
            Volatile.Write(ref _isRebuilding, 0);
            _gate.Release();
        }
    }

    private static bool TryReadVector(ProductEmbedding stored, out float[] vector)
    {
        vector = [];
        try
        {
            var parsed = JsonSerializer.Deserialize<float[]>(stored.VectorJson);
            if (parsed == null || parsed.Length != stored.Dimensions || parsed.Length == 0
                || parsed.Any(x => !float.IsFinite(x))
                || !parsed.Any(x => x != 0))
            {
                return false;
            }

            vector = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ToKnowledgeText(Product p)
    {
        return $"""
                Product: {p.Name}
                Domain: {p.CategoryDetails?.Domain}; Category: {p.CategoryDetails?.Name ?? p.Category}
                Brand: {p.BrandDetails?.Name ?? p.Brand}
                Skin types: {p.SkinTypes}; Hair types: {p.HairTypes}
                Profiles: {string.Join(", ", p.ProductProfiles.OrderBy(x => x.ProfileId).Select(x => x.Profile.Name))}
                Concerns: {p.Concerns}
                Search terms: {p.SearchKeywords} {string.Join(" ", p.ProductConcerns.OrderBy(x => x.ConcernId).Select(x => x.Concern.SearchTerms))}
                Ingredients: {p.Ingredients}
                Fragrance free declared: {(p.FragranceFreeKnown ? p.FragranceFree.ToString() : "unknown")}
                Description: {p.Description}
                Warnings: {p.Warnings}
                Usage: {p.UsageInstructions}
                Variants: {string.Join(
                    "; ",
                    p.Variants.Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => $"{x.SizeValue} {x.SizeUnit} {x.Shade} {x.Finish}"))}
                """;
    }
}
