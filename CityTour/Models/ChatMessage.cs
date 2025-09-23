namespace CityTour.Models;

public sealed class ChatMessage
{
    public ChatMessage(string message, bool isUser)
    {
        Message = message;
        IsUser = isUser;
    }

    public string Message { get; }

    public bool IsUser { get; }
}
