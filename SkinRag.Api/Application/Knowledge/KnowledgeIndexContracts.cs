namespace SkinRag.Api.Application.Knowledge;

public sealed record KnowledgeSnapshot(IReadOnlyList<KnowledgeDocument> Documents, bool IsReady, DateTime? UpdatedAtUtc);

public sealed record KnowledgeIndexStatus(
    int IndexedProducts,
    bool IsReady,
    DateTime? UpdatedAtUtc,
    bool IsRebuilding,
    int ProcessedProducts,
    int TotalProducts,
    int CachedProducts,
    string EmbeddingModel,
    string EmbeddingVersion,
    string ChatModel,
    string? LastError);
