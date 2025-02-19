using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Text.Json;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0050 

namespace generate_embeddings
{
    public class Function1
    {
        //private readonly ILogger<Function1> _logger;
        private readonly ILogger _logger;

        private readonly IConfiguration _config;
        private readonly Kernel _kernel;
        private readonly ITextEmbeddingGenerationService _embeddingService;
        private readonly KernelPlugin _memory;
        private readonly string _memoryCollectionName = "sk-documentation2";

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
            _config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            // Initialize Semantic Kernel and Redis
            string AOAI_embeddingDeploymentName = _config["AOAIembeddingDeploymentName"];
            string AOAI_endPoint = _config["AOAIendPoint"];
            string AOAI_apiKey = _config["AOAIapiKey"];
            string REDIS_connectionString = _config["REDISconnectionString"];

            var builder = Kernel
                .CreateBuilder()
                .AddAzureOpenAITextEmbeddingGeneration(AOAI_embeddingDeploymentName, AOAI_endPoint, AOAI_apiKey);

            _kernel = builder.Build();
            _embeddingService = _kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

            ConnectionMultiplexer connection = ConnectionMultiplexer.Connect(REDIS_connectionString);
            IDatabase db = connection.GetDatabase();
            RedisMemoryStore _memoryStore = new RedisMemoryStore(db);

            // Initialize a SemanticTextMemory using the memory store and embedding generation service.
            SemanticTextMemory textMemory = new(_memoryStore, _embeddingService);

            // Initialize a TextMemoryPlugin using the text memory.
            TextMemoryPlugin memoryPlugin = new(textMemory);

            // Import the text memory plugin into the Kernel.
            _memory = _kernel.ImportPluginFromObject(memoryPlugin);
        }

        [Function(nameof(Function1))]
        public async Task Run([BlobTrigger("chatfile/{name}", Connection = "AzureStorageConnectionString")] Stream stream, string name)
        {
            using var blobStreamReader = new StreamReader(stream);
            var content = await blobStreamReader.ReadToEndAsync();
            _logger.LogInformation($"C# Blob trigger function Processed blob\n Name: {name} \n Data: {content}");

            // Split content into paragraphs
            var paragraphs = content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            _logger.LogInformation($"Total number of paragrahs: {paragraphs.Length}");

            int i = 1;

            foreach (var paragraph in paragraphs)
            {
                _logger.LogInformation($"Entering loop for creating embeddings. iteration {i}");
                // Save a memory with the Kernel.
                FunctionResult result = await _kernel.InvokeAsync(
                    _memory["Save"],
                    new()
                    {
                        [TextMemoryPlugin.InputParam] = paragraph,
                        [TextMemoryPlugin.CollectionParam] = _memoryCollectionName,
                        [TextMemoryPlugin.KeyParam] = "paragraph"+i,
                    }
                );

                // If memories are recalled, the function result can be deserialized as a string[].
                string? resultStr = result.GetValue<string>();
                string[]? parsedResult = string.IsNullOrEmpty(resultStr)
                    ? null
                    : JsonSerializer.Deserialize<string[]>(resultStr);
                _logger.LogInformation(
                    $"Saved memories: {(parsedResult?.Length > 0 ? resultStr : "Paragraph uploaded")}"
                );

                i++;
                Thread.Sleep(12000);
            }
        }
    }
}
