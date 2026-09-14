using Microsoft.EntityFrameworkCore;
using NflPicks.Web.Components;
using NflPicks.Web.Data;
using NflPicks.Web.Services.Espn;
using NflPicks.Web.Services.ESPN;
using Npgsql;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var cs = builder.Configuration.GetConnectionString("Picks")
    ?? throw new InvalidOperationException("ConnectionStrings:Picks is missing.");

var dataSource = new NpgsqlDataSourceBuilder(cs).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<PicksDbContext>(o => o.UseNpgsql(dataSource));

builder.Services.AddHttpClient<EspnScoreboardClient>();
//------------------------------------------------------
//builder.Services.AddHttpClient<EspnScoreboardClient>(c =>
//{
//    c.DefaultRequestHeaders.UserAgent.ParseAdd(
//    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
//    "(KHTML, like Gecko) Chrome/120.0 Safari/537.36");
//    c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
//    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
//    c.DefaultRequestHeaders.Referrer = new Uri("https://www.espn.com/");
//});
//------------------------------------------------------

//builder.Services.AddHttpClient<EspnScoreboardClient>(c =>
//{
//    c.DefaultRequestVersion = System.Net.HttpVersion.Version20;
//    c.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrHigher;
//    c.DefaultRequestHeaders.UserAgent.ParseAdd(
//        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
//        "(KHTML, like Gecko) Chrome/120.0 Safari/537.36");
//    c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
//    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
//    c.DefaultRequestHeaders.Referrer = new Uri("https://www.espn.com/");
//});
//------------------------------------------------------

builder.Services.AddScoped<ScheduleIngestService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
