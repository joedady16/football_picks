using Microsoft.EntityFrameworkCore;
using NflPicks.Web.Components;
using NflPicks.Web.Data;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var cs = builder.Configuration.GetConnectionString("Picks")
    ?? throw new InvalidOperationException("ConnectionStrings:Picks is missing.");

var dataSource = new NpgsqlDataSourceBuilder(cs).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContextFactory<PicksDbContext>(o => o.UseNpgsql(dataSource));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
