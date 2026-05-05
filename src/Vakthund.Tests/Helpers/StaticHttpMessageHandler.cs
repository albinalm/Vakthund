using System.Net;
using System.Net.Http;

namespace Vakthund.Tests.Helpers;

public class StaticHttpMessageHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.ToString() ?? "";
        if (!responses.TryGetValue(url, out string? body))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        });
    }
}
