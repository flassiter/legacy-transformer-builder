using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace LegacyTransformerBuilder
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                // Initialize app configuration and settings
                var config = ConfigurationManager.Initialize();

                // Ensure directories exist
                DirectoryManager.EnsureDirectoriesExist(config);

                // Read enterprise domains JSON
                string enterpriseDomainsJSON = await File.ReadAllTextAsync(config.EnterpriseDomainsJSON);

                // Process all files
                var fileProcessor = new FileProcessor(config, enterpriseDomainsJSON);
                await fileProcessor.ProcessAllFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical application error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    #region Configuration

    public class AppConfiguration
    {
        public string SourceFolderPath { get; set; }
        public string OutputFolderPath { get; set; }
        public string ArchiveFolderPath { get; set; }
        public string EnterpriseDomainsJSON { get; set; }
        public string ModelPrompt { get; set; }
        public string ModelId { get; set; }
        public string AwsProfile { get; set; }
        public string AwsRegion { get; set; }
        public int MaxTokens { get; set; }
    }

    public static class ConfigurationManager
    {
        public static AppConfiguration Initialize()
        {
            // Build configuration with fallback options
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables() // Fallback to environment variables
                .Build();

            var config = new AppConfiguration
            {
                SourceFolderPath = configuration["SourceFolderPath"] ?? "./source",
                OutputFolderPath = configuration["OutputFolderPath"] ?? "./output",
                ArchiveFolderPath = configuration["ArchiveFolderPath"] ?? "./archive",
                EnterpriseDomainsJSON = configuration["EnterpriseDomainsJSON"] ?? "enterpriseDomains.json",
                ModelPrompt = configuration["ModelPrompt"] ?? "Analyze this code and provide a detailed report:",
                ModelId = configuration["ModelId"] ?? "anthropic.claude-v2",
                AwsProfile = configuration["AwsProfile"] ?? "innovate",
                AwsRegion = configuration["AwsRegion"] ?? "us-east-1",
                MaxTokens = int.Parse(configuration["MaxTokens"] ?? "4000")
            };

            // Log configuration values for debugging
            Console.WriteLine($"Using source folder: {config.SourceFolderPath}");
            Console.WriteLine($"Using output folder: {config.OutputFolderPath}");
            Console.WriteLine($"Using archive folder: {config.ArchiveFolderPath}");
            Console.WriteLine($"Using model: {config.ModelId}");

            return config;
        }
    }

    public static class DirectoryManager
    {
        public static void EnsureDirectoriesExist(AppConfiguration config)
        {
            EnsureDirectoryExists(config.SourceFolderPath);
            EnsureDirectoryExists(config.OutputFolderPath);
            EnsureDirectoryExists(config.ArchiveFolderPath);
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Console.WriteLine($"Created directory: {path}");
            }
        }
    }

    #endregion

    #region File Processing

    public class FileProcessor
    {
        private readonly AppConfiguration _config;
        private readonly string _enterpriseDomainsJSON;
        private readonly BedrockClient _bedrockClient;

        public FileProcessor(AppConfiguration config, string enterpriseDomainsJSON)
        {
            _config = config;
            _enterpriseDomainsJSON = enterpriseDomainsJSON;
            _bedrockClient = new BedrockClient(config);
        }

        public async Task ProcessAllFiles()
        {
            string[] files = Directory.GetFiles(_config.SourceFolderPath);
            Console.WriteLine($"Found {files.Length} files to process");

            foreach (string filePath in files)
            {
                await ProcessSingleFile(filePath);
            }
        }

        private async Task ProcessSingleFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string outputFilePath = Path.Combine(_config.OutputFolderPath, Path.GetFileNameWithoutExtension(fileName) + ".json");
            string archiveFilePath = Path.Combine(_config.ArchiveFolderPath, fileName);

            try
            {
                Console.WriteLine($"Processing file: {filePath}");

                // Read and deserialize the source file
                string requestFileString = await File.ReadAllTextAsync(filePath);
                AnalysisRequest analysisRequest = JsonConvert.DeserializeObject<AnalysisRequest>(requestFileString);
                analysisRequest.EnterpriseDomainsJSON = _enterpriseDomainsJSON;

                // Get analysis from LLM
                string analysisText = await _bedrockClient.GetAnalysisFromLLM(analysisRequest, _config.ModelPrompt);
                Console.WriteLine($"Response from Bedrock: ", analysisText);

                // Extract and parse the response
                //string extractedJsonString = JsonExtractor.ExtractJsonString(analysisText);
                if (string.IsNullOrEmpty(analysisText))
                {
                    throw new InvalidOperationException("Failed to receive valid JSON from the LLM response");
                }

                AnalysisResponse analysisResponse = JsonConvert.DeserializeObject<AnalysisResponse>(analysisText);

                // Create and save output
                var output = new Output
                {
                    ObjectName = analysisResponse.objectName,
                    ObjectType = analysisResponse.objectType,
                    LevelOneDomain = analysisResponse.levelOneDomain,
                    LevelTwoDomain = analysisResponse.levelTwoDomain,
                    Documentation = analysisResponse.documentation.programDescription
                };

                string outputJsonString = JsonConvert.SerializeObject(output, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                });

                await File.WriteAllTextAsync(outputFilePath, outputJsonString);
                Console.WriteLine($"LLM output written to: {outputFilePath}");

                // Move the processed file to archive
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

    #endregion

    #region Bedrock Integration

    public class BedrockClient
    {
        private readonly AmazonBedrockRuntimeClient _client;
        private readonly AppConfiguration _config;

        public BedrockClient(AppConfiguration config)
        {
            _config = config;

            RegionEndpoint region = null;
            if (!string.IsNullOrEmpty(config.AwsRegion))
            {
                region = RegionEndpoint.GetBySystemName(config.AwsRegion);
            }

            var credentials = new StoredProfileAWSCredentials(config.AwsProfile);
            var clientConfig = new AmazonBedrockRuntimeConfig
            {
                RegionEndpoint = region ?? RegionEndpoint.USEast1
            };

            _client = new AmazonBedrockRuntimeClient(credentials, clientConfig);
        }

        public async Task<string> GetAnalysisFromLLM(AnalysisRequest analysisRequest, string modelPrompt)
        {
            // Build full prompt
            var analysisRequestString = JsonConvert.SerializeObject(analysisRequest);
            var fullPrompt = $"{modelPrompt}\n\n{analysisRequestString}\n\n";

            // Prepare the request body
            
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = _config.MaxTokens,
                top_k = 250,
                stop_sequences = new string[] { },
                temperature = 1,
                top_p = 0.999,
                messages = new[]
            {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { type = "text", text = fullPrompt }
                        }
                    }
                }
            };

            string requestJSONString = JsonConvert.SerializeObject(requestBody);
            using var requestJSONStream = new MemoryStream(Encoding.UTF8.GetBytes(requestJSONString));

            // Create API request
            var request = new InvokeModelRequest
            {
                ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
                ContentType = "application/json",
                Accept = "application/json",
                Body = requestJSONStream
            };

            Console.WriteLine("Sending request to Bedrock...");
            InvokeModelResponse response = await _client.InvokeModelAsync(request);

            // Parse the response
            string responseBodyJson = new StreamReader(response.Body).ReadToEnd();
            BedrockResponse bedrockResponse = JsonConvert.DeserializeObject<BedrockResponse>(responseBodyJson);

            if (bedrockResponse?.Content == null || bedrockResponse.Content.Count == 0)
            {
                throw new InvalidOperationException("Received empty or invalid response from Bedrock");
            }

            return bedrockResponse.Content[0].Text;
        }
    }

    #endregion

    #region Utilities

    public static class JsonExtractor
    {
        public static string ExtractJsonString(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            // Use a regular expression to find the JSON within a code block or directly
            string jsonPattern = @"```json\s*({[\s\S]*?})\s*```|({[\s\S]*?})";
            Match match = Regex.Match(text, jsonPattern);

            if (match.Success)
            {
                string jsonString = match.Groups[1].Value; // Try the code block match first
                if (string.IsNullOrEmpty(jsonString))
                {
                    jsonString = match.Groups[2].Value; // If code block fails, try direct match
                }
                return jsonString;
            }
            return null;
        }
    }

    #endregion

    #region Models

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

    public class AnalysisRequest
    {
        public required Metadata Metadata { get; set; }
        public required string SourceCode { get; set; }
        public string EnterpriseDomainsJSON { get; set; }
    }

    public class AnalysisResponse
    {
        public string objectName { get; set; }
        public string objectType { get; set; }
        public string levelOneDomain { get; set; }
        public string levelTwoDomain { get; set; }
        public Documentation documentation { get; set; }
    }

    public class Documentation
    {
        public string programDescription { get; set; }
        public string businessPurpose { get; set; }
        public List<string> keyFunctionality { get; set; }
        public List<string> inputParameters { get; set; }
        public List<string> outputParameters { get; set; }
        public ScreenDetails screenDetails { get; set; }
        public List<string> programFlow { get; set; }
        public string errorHandling { get; set; }
        public string integrationPoints { get; set; }
        public string maintenanceConsiderations { get; set; }
    }

    public class ScreenDetails
    {
        public string screenName { get; set; }
        public List<string> fields { get; set; }
        public List<string> validationRules { get; set; }
    }

    public class Action
    {
        public string Type { get; set; }
        public string Description { get; set; }
    }

    public class Parameter
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string DefaultValue { get; set; }
    }

    public class Message
    {
        public string MessageId { get; set; }
        public string Description { get; set; }
    }

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

    public class Output
    {
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
        public string LevelOneDomain { get; set; }
        public string LevelTwoDomain { get; set; }
        public string Documentation { get; set; }
    }

    #endregion
}
