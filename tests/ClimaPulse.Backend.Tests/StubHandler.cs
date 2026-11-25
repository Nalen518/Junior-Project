using System.Net;

namespace ClimaPulse.Backend.Tests;

/// <summary>Answers every HTTP request with a fixed response or exception, so tests use no network. Counts calls.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body = string.Empty;
    private readonly Exception? _error;

    public int Calls { get; private set; }

    public StubHandler(HttpStatusCode status, string body) => (_status, _body) = (status, body);
    public StubHandler(Exception error) => _error = error;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return _error is not null
            ? Task.FromException<HttpResponseMessage>(_error)
            : Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }
}
