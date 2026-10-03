using Microsoft.EntityFrameworkCore;
using SkinRag.Api.Infrastructure.Persistence;
using SkinRag.Api.Application.Abstractions;

namespace SkinRag.Api.Hosting;

public static class WebApplicationExtensions
{
    public static WebApplication UseSkinRag(this WebApplication app)
    {
        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapSkinRagEndpoints(this WebApplication app)
    {
        app.MapControllers();
        app.MapGet("/", () => Results.Redirect("/chat"));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready",
            async (IDbContextFactory<SkinRagDbContext> factory, IKnowledgeIndex index, CancellationToken ct) =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var databaseReady = await db.Database.CanConnectAsync(ct);
                var knowledgeReady = index.Snapshot().IsReady;
                return Results.Json(new
                {
                    databaseReady,
                    knowledgeReady
                }, statusCode: databaseReady && knowledgeReady ? 200 : 503);
            });
        return app;
    }
}
