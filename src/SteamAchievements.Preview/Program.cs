using Microsoft.Extensions.Logging;
using SteamAchievements.Core.Diagnostics;
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
// "Default" rule (with no provider alias, so it applies to whichever
// provider is registered below too), so that fallback is never reached and
// the configured level always wins. Microsoft.AspNetCore's own noise is
// capped at Warning by the same config section, which predates this change
// and needed no code-side AddFilter to match it.
//
// ClearProviders, then ConsoleLogProvider, not the stock console/debug/
// event-source providers CreateBuilder registers by default: those know
// nothing about Redaction, so every ILogger<T> resolved here would bypass
// the shared format-then-scrub path entirely. There is no live leak today —
// this host drives fixtures and never holds a real Steam Web API key — but
// the design's own rule is structural, not "unless it's just a preview": the
// preview exists specifically so every call site this branch added is
// exercised on macOS, and a stock provider here would mean none of them ever
// touch LogLine.Format or Redaction.Scrub before the first Windows run.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new ConsoleLogProvider(() => DateTimeOffset.UtcNow));

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
