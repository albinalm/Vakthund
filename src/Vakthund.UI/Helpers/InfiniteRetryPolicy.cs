using Microsoft.AspNetCore.SignalR.Client;

namespace Vakthund.UI.Helpers;

internal sealed class InfiniteRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Defaults =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        Defaults[Math.Min(retryContext.PreviousRetryCount, Defaults.Length - 1)];
}
