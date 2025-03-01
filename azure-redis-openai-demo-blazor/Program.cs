using azure_redis_openai_demo_blazor.Client.Pages;
using azure_redis_openai_demo_blazor.Components;
using Microsoft.AspNetCore.ResponseCompression;
using azure_redis_openai_demo_blazor.Hubs;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Redis;
using StackExchange.Redis;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Azure.Identity;
using Azure.Storage.Blobs;

#pragma warning disable SKEXP0010
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0027
#pragma warning disable SKEXP0052
#pragma warning disable SKEXP0050

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSignalR();

builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

builder.Services.AddKernel();

builder.Services.AddAzureOpenAIChatCompletion(builder.Configuration["AOAIdeploymentName"], builder.Configuration["AOAIendPoint"], builder.Configuration["AOAIapiKey"]);

builder.Services.AddAzureOpenAITextEmbeddingGeneration(builder.Configuration["AOAIembeddingDeploymentName"], builder.Configuration["AOAIendPoint"], builder.Configuration["AOAIapiKey"]);

// Register Redis connection and memory store
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration["REDISconnectionString"]));
builder.Services.AddSingleton(sp =>
    new RedisMemoryStore(sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase()));

//Create memory plugin object
var sp = builder.Services.BuildServiceProvider();
SemanticTextMemory textMemory = new(sp.GetRequiredService<RedisMemoryStore>(), sp.GetRequiredService<ITextEmbeddingGenerationService>());
var memoryPlugin = new TextMemoryPlugin(textMemory);
// Import the text memory plugin into the Kernel.
var kernel = sp.GetRequiredService<Kernel>();
KernelPlugin memory = kernel.ImportPluginFromObject(memoryPlugin);
//Add DI
builder.Services.AddSingleton<KernelPlugin>(memory);

builder.Services.AddSingleton<ChatAgent>();

// builder.Services.AddScoped<ChatAgent>();

var blobServiceClient = new BlobServiceClient(new Uri(builder.Configuration["AzureStorageConnectionString"]), new DefaultAzureCredential());
var containerClient = blobServiceClient.GetBlobContainerClient(builder.Configuration["AzureStorageContainerName"]);

builder.Services.AddSingleton(containerClient);

builder.Services.AddBlazorBootstrap();

var app = builder.Build();

app.UseResponseCompression();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(azure_redis_openai_demo_blazor.Client._Imports).Assembly);

app.MapHub<ChatHub>("/chathub");

app.Run();
