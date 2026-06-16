using AIDataEngg.Web.Hubs;
using AIDataEngg.Web.Services;
using Streamix.AIDataEngg;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSignalR();

builder.Services.AddAIDataEnggCore(builder.Configuration);

builder.Services.AddSingleton<PipelineBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PipelineBackgroundService>());

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapHub<PipelineHub>("/hub/pipeline");

await EnsureDbAsync(app.Services);

app.Run();

static async Task EnsureDbAsync(IServiceProvider services)
{
    var env = services.GetRequiredService<IWebHostEnvironment>();
    var appData = Path.Combine(env.ContentRootPath, "App_Data");
    Directory.CreateDirectory(appData);

    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<Streamix.AIDataEngg.Data.RssDbContext>();
    await db.Database.EnsureCreatedAsync();
}
