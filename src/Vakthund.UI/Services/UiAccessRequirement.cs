using Microsoft.AspNetCore.Authorization;

namespace Vakthund.UI.Services;

public sealed record UiAccessRequirement : IAuthorizationRequirement;
