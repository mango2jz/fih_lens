using System.Net;

namespace FihLens.Tests.Infrastructure;

internal sealed class ProviderTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }
    public TrackingContent? ResponseContent { get; private set; }
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = await respond(request, cancellationToken);
        ResponseContent = response.Content as TrackingContent;
        return response;
    }

    public static ProviderTestHandler Respond(HttpStatusCode status, string body) => new((_, _) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new TrackingContent(body) }));
}

internal sealed class TrackingContent(string content) : StringContent(content)
{
    public bool WasDisposed { get; private set; }
    protected override void Dispose(bool disposing)
    {
        if (disposing) WasDisposed = true;
        base.Dispose(disposing);
    }
}
