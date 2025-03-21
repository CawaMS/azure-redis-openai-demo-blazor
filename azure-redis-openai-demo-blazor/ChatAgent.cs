﻿using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using StackExchange.Redis;
using System.Data.Common;
using Redis.OM.Contracts;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0027
#pragma warning disable SKEXP0052
#pragma warning disable SKEXP0050

public class ChatAgent(Kernel kernel, KernelPlugin memory, IChatCompletionService chatCompletionService, ITextEmbeddingGenerationService embeddingService,IConnectionMultiplexer connectionMultiplexer,ISemanticCache semanticCache, IConfiguration config)
{
    // private static ChatHistory history = new();

    const string Deliminater = "_&_";
    const string KeyPrefix = "ChatHistory_";


    public async Task<string> CompleteChat(string user, string userInput)
    {
        string chatHistoryKey = KeyPrefix + Deliminater + user;

        var history = new ChatHistory();
        //TODO: implement loading chat history from redis
        List<string> chatHistory = await LoadChatHistoryAsync(chatHistoryKey);
        foreach (var message in chatHistory)
        {
            var parts = message.Split(Deliminater);
            history.AddMessage(new AuthorRole(parts[0]), parts[1]);
        }

        if (userInput is not null)
        {
            //Try to retrieve from semantic caching store
            if(semanticCache.GetSimilar(userInput).Length > 0)
            {
                var chatResponse = semanticCache.GetSimilar(userInput)[0];
                return chatResponse;
            }

            // System.Threading.Thread.Sleep(10000);

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

            // Save the user message to the chat history
            await SaveChatMessageAsync(chatHistoryKey, "user" + Deliminater + userInput);
        }

        // Get response from the AI
        ChatMessageContent result = chatCompletionService.GetChatMessageContentAsync(history, kernel: kernel).Result;

        // System.Threading.Thread.Sleep(10000);

        // Add the message from the agent to the chat history
        history.AddMessage(result.Role, result.Content ?? string.Empty);

        // Console.WriteLine("TestAuthorRole: " + result.Role.ToString());

        // Save the agent message to the chat history
        await SaveChatMessageAsync(chatHistoryKey, result.Role + Deliminater + result.Content);

        await semanticCache.StoreAsync(userInput, result.ToString());

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