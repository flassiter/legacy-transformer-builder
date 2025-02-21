using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public class Content
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}

public class BedrockResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Role { get; set; }
    public string? Model { get; set; }
    public List<Content>? Content { get; set; }
    public string? StopReason { get; set; }
    public object? StopSequence { get; set; }
    public Usage? Usage { get; set; }
}

public class Usage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public class Metadata
{
    public string? ObjectName { get; set; }
    public string? ObjectType { get; set; }
    public string? ObjectAttribute { get; set; }
    public string? ObjectFamily { get; set; }
    public string? ObjectDescription { get; set; }
    public DateTime ObjectFirstDefined { get; set; }
    public DateTime ObjectLastTouched { get; set; }
    public int ObjectDependencyCount { get; set; }
    public int ObjectReferencedByCount { get; set; }
}

public class RootObject
{
    public required Metadata Metadata { get; set; }
    public required string SourceCode { get; set; }
}

public class CodeAnalyzer
{
    public static async Task Main(string[] args)
    {
        // Build configuration with fallback options
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables() // Fallback to environment variables
            .Build();

        // Access configuration values with defaults
        string sourceFolderPath = configuration["SourceFolderPath"] ?? "./source";
        string outputFolderPath = configuration["OutputFolderPath"] ?? "./output";
        string archiveFolderPath = configuration["ArchiveFolderPath"] ?? "./archive";

        // Log configuration values for debugging
        Console.WriteLine($"Using source folder: {sourceFolderPath}");
        Console.WriteLine($"Using output folder: {outputFolderPath}");
        Console.WriteLine($"Using archive folder: {archiveFolderPath}");

        EnsureDirectoryExists(outputFolderPath);
        EnsureDirectoryExists(archiveFolderPath);

        await ProcessFiles(sourceFolderPath, outputFolderPath, archiveFolderPath, configuration);
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static async Task ProcessFiles(string sourceFolderPath, string outputFolderPath, string archiveFolderPath, IConfiguration configuration)
    {
        string[] files = Directory.GetFiles(sourceFolderPath);
        Console.WriteLine($"Found {files.Length} files to process");

        foreach (string filePath in files)
        {
            await ProcessFile(filePath, outputFolderPath, archiveFolderPath, configuration);
        }
    }

    private static async Task ProcessFile(string filePath, string outputFolderPath, string archiveFolderPath, IConfiguration configuration)
    {
        string fileName = Path.GetFileName(filePath);
        string outputFilePath = Path.Combine(outputFolderPath, Path.GetFileNameWithoutExtension(fileName) + ".json");
        string archiveFilePath = Path.Combine(archiveFolderPath, fileName);
        

        try
        {
            Console.WriteLine($"Processing file: {filePath}");

            // 1. Read and deserialize the file
            string jsonString = await File.ReadAllTextAsync(filePath);
            RootObject jsonObject = JsonConvert.DeserializeObject<RootObject>(jsonString);


            var credentials = new StoredProfileAWSCredentials("innovate");
            var config = new AmazonBedrockRuntimeConfig
            {
                RegionEndpoint = RegionEndpoint.USEast1
            };

            using var client = new AmazonBedrockRuntimeClient(credentials, config);

            
            // 5. Prepare the request with updated format for Claude 3
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 4000,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Analyze this code and provide a detailed report:\n\n{jsonObject.SourceCode}\n\n"
                    }
                }
            };

            string bodyString = JsonConvert.SerializeObject(requestBody);
            using var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(bodyString));

            var request = new InvokeModelRequest
            {
                ModelId = "anthropic.claude-v2",
                Body = bodyStream,
                ContentType = "application/json",
                Accept = "application/json"
            };

            
            Console.WriteLine("Sending request to Bedrock...");
            InvokeModelResponse response = await client.InvokeModelAsync(request);
            string responseBodyJson = new StreamReader(response.Body).ReadToEnd();

            
            var responseData = JsonConvert.DeserializeObject<dynamic>(responseBodyJson);
            string llmOutput = "";

            // Deserialize the JSON response
            BedrockResponse responseObject = JsonConvert.DeserializeObject<BedrockResponse>(responseBodyJson);

            string analysisText = responseObject.Content[0].Text;

            // 8. Construct the output JSON
            var outputJson = new
            {
                Metadata = jsonObject.Metadata,
                Analysis = analysisText
            };

            // 9. Serialize and save the output
            string outputJsonString = JsonConvert.SerializeObject(outputJson, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });
            await File.WriteAllTextAsync(outputFilePath, outputJsonString);
            Console.WriteLine($"LLM output written to: {outputFilePath}");

            // 10. Move the processed file to the archive
            File.Move(filePath, archiveFilePath, overwrite: true);
            Console.WriteLine($"File moved to archive: {archiveFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing file: {filePath}");
            Console.WriteLine($"Exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}