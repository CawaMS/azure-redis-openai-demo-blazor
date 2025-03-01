using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using StackExchange.Redis;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0027
#pragma warning disable SKEXP0052
#pragma warning disable SKEXP0050

public class ChatAgent(Kernel kernel, KernelPlugin memory, IChatCompletionService chatCompletionService, ITextEmbeddingGenerationService embeddingService,IConnectionMultiplexer connectionMultiplexer, IConfiguration config)
{
    // private static ChatHistory history = new();

    public async Task<string> CompleteChat(string user, string userInput)
    {
       
        var history = new ChatHistory(); 

        if (userInput is not null)
        {
            // Retrieve a memory with the Kernel.
            FunctionResult searchResult = await kernel.InvokeAsync(
                memory["Recall"],
                new()
                {
                    // TODO: make these configurable or dynamic
                    [TextMemoryPlugin.InputParam] = "Ask: "+userInput,
                    [TextMemoryPlugin.CollectionParam] = "sk-documentation2", 
                    [TextMemoryPlugin.LimitParam] = 2,
                    [TextMemoryPlugin.RelevanceParam] = 0.5
                }
            );

            // User the result to augment the prompt
            history.AddUserMessage(userInput + searchResult.GetValue<string>());
        }

        // Get response from the AI
        ChatMessageContent result = await chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel);

        // Add the message from the agent to the chat history
        history.AddMessage(result.Role, result.Content ?? string.Empty);


        return result.ToString();
    }

    public async Task SaveChatHistoryAsync(string key, List<string> history)
    {
        await SaveListAsync(key, history);
    }

    public async Task SaveChatMessageAsync(string key, string message)
    {
        var db = connectionMultiplexer.GetDatabase();
        await db.ListRightPushAsync(key, message);
    }

    public async Task<List<string>> LoadChatHistoryAsync(string key)
    {
        return await LoadListAsync(key);
    }

    private async Task SaveListAsync(string key, List<string> list)
    {
        var db = connectionMultiplexer.GetDatabase();
        foreach (var item in list)
        {
            await db.ListRightPushAsync(key, item);
        }
    }

    private async Task<List<string>> LoadListAsync(string key)
    {
        var db = connectionMultiplexer.GetDatabase();
        var redisList = await db.ListRangeAsync(key);
        return redisList.Select(x => x.ToString()).ToList();
    }

}