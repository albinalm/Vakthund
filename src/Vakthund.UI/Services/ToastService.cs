using Vakthund.UI.Enums;
using Vakthund.UI.Models;

namespace Vakthund.UI.Services;

public class ToastService
{
    private readonly List<ToastMessage> _messages = [];

    public event Action? OnChanged;

    public IReadOnlyList<ToastMessage> Messages => _messages;

    public void Show(string summary, string? detail = null, ToastColor color = ToastColor.Info, int duration = 3000) =>
        Show(new ToastMessage { Summary = summary, Detail = detail, Color = color, Duration = duration });

    public void Show(ToastMessage message)
    {
        _messages.Add(message);
        OnChanged?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        _messages.RemoveAll(m => m.Id == id);
        OnChanged?.Invoke();
    }
}
