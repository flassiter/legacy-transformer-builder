using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace legacy_transformer_builder
{

    public class Builder
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
            string enterpriseDomainsJSON = configuration["EnterpriseDomainsJSON"] ?? "enterpriseDomains.json";
            string modelPrompt = configuration["ModelPrompt"] ?? "Analyze this code and provide a detailed report:";

            // Log configuration values for debugging
            Console.WriteLine($"Using source folder: {sourceFolderPath}");
            Console.WriteLine($"Using output folder: {outputFolderPath}");
            Console.WriteLine($"Using archive folder: {archiveFolderPath}");

            EnsureDirectoryExists(outputFolderPath);
            EnsureDirectoryExists(archiveFolderPath);

            // Read domains for model prompt
            string enterpriseDomainsJSONString = await File.ReadAllTextAsync(enterpriseDomainsJSON);

            await ProcessFiles(sourceFolderPath, outputFolderPath, archiveFolderPath, modelPrompt, enterpriseDomainsJSONString);
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static async Task ProcessFiles(string sourceFolderPath, string outputFolderPath, string archiveFolderPath, string modelPrompt, string enterpriseDomainsJSONString)
        {
            string[] files = Directory.GetFiles(sourceFolderPath);
            Console.WriteLine($"Found {files.Length} files to process");

            foreach (string filePath in files)
            {
                await ProcessFile(filePath, outputFolderPath, archiveFolderPath, modelPrompt, enterpriseDomainsJSONString);
            }
        }

        private static async Task ProcessFile(string filePath, string outputFolderPath, string archiveFolderPath, string modelPrompt, string enterpriseDomainsJSONString)
        {
            string fileName = Path.GetFileName(filePath);
            string outputFilePath = Path.Combine(outputFolderPath, Path.GetFileNameWithoutExtension(fileName) + ".json");
            string archiveFilePath = Path.Combine(archiveFolderPath, fileName);

            try
            {

                // -- Read and deserialize the source file
                Console.WriteLine($"Processing file: {filePath}");
                string requestFileString = await File.ReadAllTextAsync(filePath);

                // -- Build prompt
                AnalysisRequest analysisRequest = JsonConvert.DeserializeObject<AnalysisRequest>(requestFileString);
                analysisRequest.EnterpriseDomainsJSON = enterpriseDomainsJSONString;
                var analysisRequestString = JsonConvert.SerializeObject(analysisRequest);
                modelPrompt = modelPrompt + "//n//n" + analysisRequestString + "//n//n";

                // -- Connect to AWS Bedrock
                var credentials = new StoredProfileAWSCredentials("innovate");
                var config = new AmazonBedrockRuntimeConfig
                {
                    RegionEndpoint = RegionEndpoint.USEast1
                };

                using var client = new AmazonBedrockRuntimeClient(credentials, config);

                // -- Prepare the request
                var requestBody = new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 4000,
                    messages = new[]
                    {
                    new
                    {
                        role = "user",
                        content = modelPrompt
                    }
                }
                };

                string requestJSONString = JsonConvert.SerializeObject(requestBody);
                using var requestJSONStream = new MemoryStream(Encoding.UTF8.GetBytes(requestJSONString));

                // -- Submit the request to AWS Bedrock
                var request = new InvokeModelRequest
                {
                    ModelId = "anthropic.claude-v2",
                    Body = requestJSONStream,
                    ContentType = "application/json",
                    Accept = "application/json"
                };

                Console.WriteLine("Sending request to Bedrock...");
                InvokeModelResponse response = await client.InvokeModelAsync(request);

                // -- Receive and parse response from Bedrock
                string responseBodyJson = new StreamReader(response.Body).ReadToEnd();
                BedrockResponse bedrockResponse = JsonConvert.DeserializeObject<BedrockResponse>(responseBodyJson);
                string analysisText = bedrockResponse.Content[0].Text;

                var extractedJsonString = ExtractJsonString(analysisText);
                AnalysisResponse analysisResponse = JsonConvert.DeserializeObject<AnalysisResponse>(extractedJsonString);


                // -- Create output response
                var output = new Output
                {
                    ObjectName = analysisResponse.ObjectName,
                    ObjectType = analysisResponse.ObjectType,
                    LevelOneDomain = analysisResponse.LevelOneDomain,
                    LevelTwoDomain = analysisResponse.LevelTwoDomain,
                    Documentation = analysisResponse.Documentation.Description

                };

                string outputJsonString = JsonConvert.SerializeObject(output, new JsonSerializerSettings
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

        public static string ExtractJsonString(string text)
        {
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
}