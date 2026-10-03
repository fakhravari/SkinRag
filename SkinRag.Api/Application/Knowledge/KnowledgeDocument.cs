using System.Collections.Frozen;
using SkinRag.Api.Application.Common.Text;

namespace SkinRag.Api.Application.Knowledge;

public sealed record KnowledgeDocument(int ProductId, string Content, float[] Embedding)
{
    public FrozenSet<string> Tokens { get; } = PersianText.SearchTokens(Content);
    public double VectorNorm { get; } = Math.Sqrt(Embedding.Sum(value => (double)value * value));
}
