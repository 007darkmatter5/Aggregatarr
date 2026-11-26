using System.Net;
using System.Text;
using Aggregatarr.Connectors;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Tests;

internal static class Fixtures
{
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

internal sealed record RecordedCall(string Method, string Path, string? Body, IReadOnlyDictionary<string, string> Headers);

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _next;
    public List<RecordedCall> Calls { get; } = [];

    public ScriptedHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> next) => _next = next;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Calls.Add(new RecordedCall(request.Method.Method, request.RequestUri?.PathAndQuery ?? "", body, headers));
        return _next(request, body);
    }
}

internal static class HttpJson
{
    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string text, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
}

internal sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception));
        if (exception is not null)
            Lines.Add(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

internal sealed class ListLoggerProvider : ILoggerProvider
{
    public ListLogger Logger { get; } = new();
    public ILogger CreateLogger(string categoryName) => Logger;
    public void Dispose() { }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class ConnectorHarness : IDisposable
{
    public ScriptedHandler Handler { get; }
    public HttpClient Http { get; }
    public ILoggerFactory Factory { get; }
    public ListLogger Logs { get; }
    public const string ApiKey = "super-secret-api-key-do-not-leak";
    public const string Password = "super-secret-password-do-not-leak";

    public ConnectorHarness(Func<HttpRequestMessage, string?, HttpResponseMessage> next)
    {
        Handler = new ScriptedHandler(next);
        Http = new HttpClient(Handler) { BaseAddress = new Uri("http://media.example:8989/") };
        var provider = new ListLoggerProvider();
        Logs = provider.Logger;
        Factory = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
    }

    public ConnectorContext Context(string? apiKey = ApiKey, string? username = null, string? password = null) => new()
    {
        Http = Http,
        BaseUri = new Uri("http://media.example:8989/"),
        ApiKey = apiKey,
        Username = username,
        Password = password,
        CalendarStart = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        CalendarEnd = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)
    };

    public void AssertNoSecrets()
    {
        var blob = string.Join('\n', Logs.Lines);
        Assert.DoesNotContain(ApiKey, blob);
        Assert.DoesNotContain(Password, blob);
    }

    public void Dispose()
    {
        Http.Dispose();
        Factory.Dispose();
    }
}
