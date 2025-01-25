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

public class ChatAgent
{
    IConfiguration config;
    public ChatAgent(IConfiguration _config)
    {
        config = _config;
    }

    public async Task<string> CompleteChat(string userInput)
    {
        string AOAI_deploymentName = config["AOAIdeploymentName"] ?? "";
        string AOAI_endPoint = config["AOAIendPoint"] ?? "";
        string AOAI_apiKey = config["AOAIapiKey"] ?? "";
        string AOAI_embeddingDeploymentName = config["AOAIembeddingDeploymentName"] ?? "";
        string REDIS_connectionString = config["REDISconnectionString"] ?? "";

        var builder = Kernel
                        .CreateBuilder()
                        .AddAzureOpenAITextEmbeddingGeneration(AOAI_embeddingDeploymentName, AOAI_endPoint, AOAI_apiKey)
                        .AddAzureOpenAIChatCompletion(AOAI_deploymentName, AOAI_endPoint, AOAI_apiKey);

        // Add Enterprise components
        builder.Services.AddLogging();

        // Build the kernel
        Kernel kernel = builder.Build();

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        // Retrieve the embedding service from the Kernel.
        ITextEmbeddingGenerationService embeddingService = kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

        // Initialize a memory store using the redis database
        ConnectionMultiplexer connection = ConnectionMultiplexer.Connect(REDIS_connectionString);
        IDatabase _db = connection.GetDatabase();
        RedisMemoryStore memoryStore = new RedisMemoryStore(_db);

        // Initialize a SemanticTextMemory using the memory store and embedding generation service.
        SemanticTextMemory textMemory = new(memoryStore, embeddingService);

        // Initialize a TextMemoryPlugin using the text memory.
        TextMemoryPlugin memoryPlugin = new(textMemory);

        // Import the text memory plugin into the Kernel.
        KernelPlugin memory = kernel.ImportPluginFromObject(memoryPlugin);

        // Create a history store the conversation
        //TODO: add user and session
        //TODO: move the chat history to Redis
        var history = new ChatHistory(); 

        if (userInput is not null)
        {
            // Retrieve a memory with the Kernel.
            FunctionResult searchResult = await kernel.InvokeAsync(
                memory["Recall"],
                new()
                {
                    [TextMemoryPlugin.InputParam] = "Ask: "+userInput,
                    [TextMemoryPlugin.CollectionParam] = "sk-documentation2", // TODO: make this configurable or dynamic
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



}