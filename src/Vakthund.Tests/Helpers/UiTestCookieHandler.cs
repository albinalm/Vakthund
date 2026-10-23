using System.Net;

namespace Vakthund.Tests.Helpers;

public sealed class UiTestCookieHandler(CookieContainer cookies, HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string header = cookies.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(header))
            request.Headers.TryAddWithoutValidation("Cookie", header);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            foreach (string value in values)
                cookies.SetCookies(request.RequestUri!, value);
        }
        return response;
    }
}
