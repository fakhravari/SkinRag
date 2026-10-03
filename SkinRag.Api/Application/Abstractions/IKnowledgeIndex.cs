using SkinRag.Api.Application.Knowledge;

namespace SkinRag.Api.Application.Abstractions;

public interface IKnowledgeIndex
{
    KnowledgeSnapshot Snapshot();
    KnowledgeIndexStatus Status();
    Task RebuildAsync(CancellationToken cancellationToken = default, bool force = false);
}
