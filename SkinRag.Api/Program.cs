using SkinRag.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSkinRag(builder.Configuration);
var app = builder.Build();
app.UseSkinRag();
app.MapSkinRagEndpoints();
app.Run();
