using Microsoft.Extensions.Caching.Memory;

namespace SkinRag.Api.Application.Consultation;

public sealed record ConversationState(Guid Id, string[] UserQuestions, int[] ProductIds, DateTime UpdatedAtUtc);

// Product references come only from validated server results, never client assistant history.
public sealed class ConversationStore : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });
    public ConversationState Read(Guid? id) => id.HasValue && _cache.TryGetValue(id.Value, out ConversationState? state) && state is not null
        ? state : new(Guid.NewGuid(), [], [], DateTime.UtcNow);

    public void Save(ConversationState previous, string question, IEnumerable<int> ids)
    {
        var next = previous with
        {
            UserQuestions = previous.UserQuestions.Append(question).TakeLast(4).ToArray(),
            ProductIds = ids.Distinct().Take(10).ToArray(), UpdatedAtUtc = DateTime.UtcNow
        };
        _cache.Set(next.Id, next, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(30) });
    }

    public static bool IsRepeated(ConversationState state, string question) =>
        DateTime.UtcNow - state.UpdatedAtUtc < TimeSpan.FromSeconds(30) && state.UserQuestions.Length >= 3 &&
        state.UserQuestions.TakeLast(3).All(x => x == question);
    public void Dispose() => _cache.Dispose();
}
