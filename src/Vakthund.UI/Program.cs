using Vakthund.UI.Extensions;
using Vakthund.UI.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ApplyEnvironmentOverrides();
builder.Services.AddVakthundUi(builder.Configuration);

WebApplication app = builder.Build();

app.Services.GetRequiredService<AuditHubConnection>().Start();
app.UseVakthundUI();

app.Run();
