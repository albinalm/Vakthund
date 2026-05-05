using Microsoft.Extensions.Options;
using Vakthund.UI.Enums;
using Vakthund.UI.Extensions;
using Vakthund.UI.Options;
using Vakthund.UI.Services;
using Vakthund.UI.Services.Interfaces;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ApplyEnvironmentOverrides();
builder.Services.AddVakthundUi(builder.Configuration);

WebApplication app = builder.Build();

UiOptions uiOptions = app.Services.GetRequiredService<IOptions<UiOptions>>().Value;
if (uiOptions.StorageMode == StorageMode.Disk)
{
    IAuditStore auditStore = app.Services.GetRequiredKeyedService<IAuditStore>(StorageMode.Disk);
    app.Services.GetRequiredService<MetricsStore>().InitializeFromDisk(auditStore.Snapshot());
}

app.Services.GetRequiredService<AuditHubConnection>().Start();

app.UseVakthundUI();

app.Run();
