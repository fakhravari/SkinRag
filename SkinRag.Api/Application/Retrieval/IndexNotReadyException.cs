namespace SkinRag.Api.Application.Retrieval;

public sealed class IndexNotReadyException(string message) : Exception(message);
