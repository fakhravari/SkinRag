namespace SkinRag.Api.Application.Abstractions;

public sealed class InvalidModelOutputException(string message) : Exception(message);
