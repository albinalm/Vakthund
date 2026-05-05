using System.Net.Http;

namespace Vakthund.Tests.Helpers;

public class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
