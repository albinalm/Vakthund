using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Extensions;

public static class UiAuthenticationServiceCollectionExtensions
{
    public const string CookieScheme = "VakthundCookie";
    public const string OidcScheme = "VakthundOidc";
    public static IServiceCollection AddUiAuthentication(this IServiceCollection services, IConfiguration config)
    {
        UiAuthenticationOptions settings = config.GetSection("UI:Authentication").Get<UiAuthenticationOptions>() ?? new UiAuthenticationOptions();
        settings.Validate();
        services.AddSingleton<IOptions<UiAuthenticationOptions>>(Microsoft.Extensions.Options.Options.Create(settings));
        services.AddSingleton<UiAccountAccess>();
        services.AddSingleton<IAuthorizationHandler>(provider => provider.GetRequiredService<UiAccountAccess>());
        services.AddCascadingAuthenticationState();
        services.AddAuthorization(options =>
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder().AddRequirements(new UiAccessRequirement()).Build();
            options.AddPolicy(UiAccountAccess.PolicyName, policy);
            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
        });
        AuthenticationBuilder authentication = services.AddAuthentication();
        if (!settings.Enabled)
            return services;
        string clientSecret = settings.ClientSecret;
        if (!string.IsNullOrEmpty(settings.ClientSecretFile))
        {
            clientSecret = File.ReadAllText(settings.ClientSecretFile).TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("UI:Authentication:ClientSecretFile must contain a non-empty client secret.");
        }
        services.AddMemoryCache();
        services.AddSingleton<UiSessionStore>();
        services.AddScoped<AuthenticationStateProvider, UiAuthenticationStateProvider>();
        authentication.AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = "__Host-vakthund";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/auth/login";
            options.AccessDeniedPath = "/auth/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(settings.SessionMinutes);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                if (!context.HttpContext.RequestServices.GetRequiredService<UiAccountAccess>().IsAllowed(context.Principal))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieScheme);
                }
            };
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/_blazor"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentLength = 0;
                }
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieScheme).Configure<UiSessionStore>((options, sessions) => options.SessionStore = sessions);
        authentication.AddOpenIdConnect(OidcScheme, options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = clientSecret;
            options.SignInScheme = CookieScheme;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.SaveTokens = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.ValidateIssuer = true;
            options.Events.OnTokenValidated = context =>
            {
                if (!context.HttpContext.RequestServices.GetRequiredService<UiAccountAccess>().IsAllowed(context.Principal))
                {
                    context.HandleResponse();
                    context.Response.Redirect(context.Request.PathBase + "/auth/access-denied");
                }
                else if (context.TokenEndpointResponse?.IdToken is { Length: > 0 } idToken)
                    context.Properties?.StoreTokens([new AuthenticationToken { Name = "id_token", Value = idToken }]);
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(context.Request.PathBase + "/auth/error");
                return Task.CompletedTask;
            };
        });
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = CookieScheme;
            options.DefaultChallengeScheme = CookieScheme;
            options.DefaultSignInScheme = CookieScheme;
        });
        return services;
    }
}
