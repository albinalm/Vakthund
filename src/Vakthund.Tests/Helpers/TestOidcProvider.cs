using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Vakthund.Tests.Helpers;

public sealed class TestOidcProvider : HttpMessageHandler
{
    public const string Issuer = "https://identity.example.test";
    public const string ClientId = "vakthund-test";
    private readonly RSA _rsa = RSA.Create(2048);
    public string Subject { get; set; } = "operator-123";
    public string Nonce { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string TokenIssuer { get; set; } = Issuer;
    public string Audience { get; set; } = ClientId;
    public bool ExpiredToken { get; set; }
    public bool UseWrongSigningKey { get; set; }
    public bool UseWrongNonce { get; set; }
    public OpenIdConnectConfiguration Configuration
    {
        get
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = Issuer,
                AuthorizationEndpoint = Issuer + "/authorize",
                TokenEndpoint = Issuer + "/token",
                EndSessionEndpoint = Issuer + "/logout"
            };
            configuration.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "test-key" });
            return configuration;
        }
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(Issuer + "/token", request.RequestUri?.ToString());
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
        Assert.Equal("authorization_code", form["grant_type"].ToString());
        string challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        Assert.Equal(CodeChallenge, challenge);
        using RSA wrongKey = RSA.Create(2048);
        var key = new RsaSecurityKey(UseWrongSigningKey ? wrongKey : _rsa) { KeyId = "test-key" };
        DateTime now = DateTime.UtcNow;
        var token = new JwtSecurityToken(TokenIssuer, Audience,
            [new Claim("sub", Subject), new Claim("name", "Test Operator"), new Claim("nonce", UseWrongNonce ? "wrong-nonce" : Nonce),
                new Claim("iat", EpochTime.GetIntDate(now).ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)],
            now.AddMinutes(-10), ExpiredToken ? now.AddMinutes(-1) : now.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        string idToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                access_token = "synthetic-access-token", token_type = "Bearer", expires_in = 300, id_token = idToken
            }), Encoding.UTF8, "application/json")
        };
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _rsa.Dispose();
        base.Dispose(disposing);
    }
}
