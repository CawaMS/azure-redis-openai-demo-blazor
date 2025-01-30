using Microsoft.AspNetCore.SignalR;

namespace azure_redis_openai_demo_blazor.Hubs; 

public class ChatHub : Hub
{
    public async Task SendMessage(string user, string message)
    {
        await Clients.Caller.SendAsync("ReceiveMessage", user, message);
    }

    public async Task SendOpenaiMessage(string message, ChatAgent chatAgent)
    { 
        string response = await chatAgent.CompleteChat(message);
        await Clients.Caller.SendAsync("ReceiveMessage", "AI Assistant", response);
    }
}
