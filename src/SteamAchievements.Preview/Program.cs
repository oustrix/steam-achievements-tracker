using SteamAchievements.Core.Presentation;
using SteamAchievements.Preview.Components;
using SteamAchievements.Preview.Fixtures;
using SteamAchievements.UI.Layout;
using SteamAchievements.UI.State;

var builder = WebApplication.CreateBuilder(args);

// The Debug-level lines this host needs to prove out (AppShell.Guard's
// "staying put" line, for one) come from appsettings.json/
// appsettings.Development.json setting Logging:LogLevel:Default to Debug,
// not from anything here. builder.Logging.SetMinimumLevel(LogLevel.Debug)
// was tried and measured to do nothing: it only sets LoggerFilterOptions's
// MinLevel, which is a fallback used solely when no LoggerFilterRule
// matches a given log call. Both appsettings files always contribute a
// "Default" rule, so that fallback is never reached and the configured
// level always wins. Microsoft.AspNetCore's own noise is capped at
// Warning by the same config section, which predates this change and
// needed no code-side AddFilter to match it.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// One fixture query per browser session, so the scenario switch in the query
// string affects only the tab that set it.
builder.Services.AddScoped<FixtureLibraryQuery>();
builder.Services.AddScoped<ILibraryQuery>(s => s.GetRequiredService<FixtureLibraryQuery>());
builder.Services.AddScoped<IUserPreferences, InMemoryUserPreferences>();

builder.Services.AddScoped<FixtureSync>();
builder.Services.AddScoped<ISyncPresenter>(s => s.GetRequiredService<FixtureSync>());
builder.Services.AddScoped<ISyncController>(s => s.GetRequiredService<FixtureSync>());

// One state for both fixtures, because production has one IAccountStore and one
// ISecretStore behind them: without this, a reset in settings leaves the key
// "stored" and AppShell's guard never redirects — the exact case it exists for.
builder.Services.AddScoped<FixtureState>();
builder.Services.AddScoped<IOnboarding, FixtureOnboarding>();
builder.Services.AddScoped<IAccountAdmin, FixtureAccountAdmin>();

// Registered concretely as well: LastLinkStrip subscribes to it to show the URL
// a button would have opened.
builder.Services.AddScoped<FixtureLinks>();
builder.Services.AddScoped<IExternalLinks>(s => s.GetRequiredService<FixtureLinks>());

builder.Services.AddScoped<LibraryChangeSignal>();

builder.Services.AddScoped<QueueState>();

// Frozen so the preview reads identically on every run.
builder.Services.AddScoped<IClock>(_ => new FixedClock(FixtureData.Now));

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

// QueuePage.razor (and every later screen) lives in the RCL, not in this
// host assembly. Router.AdditionalAssemblies in Routes.razor only covers
// client-side interactive navigation; the initial server-rendered request is
// matched by this endpoint's own route table, which by default only scans
// typeof(App).Assembly and needs to be told about the RCL explicitly.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AppShell).Assembly);

app.Run();

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now => now;
}
