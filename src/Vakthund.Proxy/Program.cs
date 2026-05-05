using Vakthund.Proxy.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ApplyEnvironmentOverrides();
builder.AddVakthundProxy();

WebApplication app = builder.Build();

app.ConfigureUrls(builder.Configuration);
app.MapManagementEndpoints(builder.Configuration);
app.UseProxyPipeline(builder.Configuration);

app.Run();
