using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using JarvisBot.Plugins;

namespace JarvisBot.Services;

public class AgentService
{
    private readonly string _ollamaEndpoint;
    private readonly string _modelId;
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly ChatHistory _history;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private static Process? _ollamaProcess = null;
    private static readonly object _lockObj = new();

    public AgentService(string ollamaEndpoint, string modelId, string? systemPrompt = null)
    {
        _ollamaEndpoint = ollamaEndpoint.TrimEnd('/');
        _modelId = modelId;

        var builder = Kernel.CreateBuilder();

        // Connect to Ollama endpoint
        builder.AddOllamaChatCompletion(
            modelId: _modelId,
            endpoint: new Uri(_ollamaEndpoint)
        );

        // Register plugins
        builder.Plugins.AddFromType<SystemControlPlugin>("SystemControl");
        builder.Plugins.AddFromType<WebNavigationPlugin>("WebNavigation");

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();

        _history = new ChatHistory();
        _history.AddSystemMessage(
            systemPrompt ??
            "You are J.A.R.V.I.S., a direct, concise, and efficient desktop AI assistant. " +
            "When the user requests an action, use the tool syntax: [TOOL_CALL: PluginName-FunctionName {\"param\": \"value\"}]. " +
            "Available tools: " +
            "- SystemControl-LaunchAndTypeText {\"applicationName\": \"string\", \"text\": \"string\"} " +
            "- SystemControl-TypeText {\"text\": \"string\"} " +
            "- SystemControl-WriteToFileAndOpen {\"fileName\": \"string\", \"content\": \"string\"} " +
            "- SystemControl-LaunchApplication {\"applicationName\": \"string\"} " +
            "- SystemControl-LockWorkstation {} " +
            "- SystemControl-OpenWebsite {\"url\": \"string\"} " +
            "- SystemControl-GetCurrentTime {} " +
            "- WebNavigation-SearchWeb {\"query\": \"string\"} " +
            "- WebNavigation-OpenYouTube {\"query\": \"string\"}. " +
            "If no tool is needed, reply directly in under 2 sentences."
        );
    }

    /// <summary>
    /// Checks if Ollama is running and responsive. If not and running locally, attempts to auto-start it.
    /// </summary>
    public async Task<bool> EnsureOllamaRunningAsync()
    {
        if (await IsOllamaHealthyAsync())
        {
            return true;
        }

        var endpointUri = new Uri(_ollamaEndpoint);
        if (endpointUri.Host != "localhost" && endpointUri.Host != "127.0.0.1")
        {
            return false;
        }

        string? exePath = FindOllamaExecutable();
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("[AgentService] Ollama executable not found on host machine.");
            return false;
        }

        lock (_lockObj)
        {
            if (_ollamaProcess == null || _ollamaProcess.HasExited)
            {
                try
                {
                    Console.WriteLine($"[AgentService] Auto-starting Ollama daemon: {exePath} serve");
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "serve",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _ollamaProcess = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AgentService] Failed to start Ollama: {ex.Message}");
                    return false;
                }
            }
        }

        // Wait up to 12 seconds for Ollama to begin responding on the endpoint
        for (int i = 0; i < 24; i++)
        {
            await Task.Delay(500);
            if (await IsOllamaHealthyAsync())
            {
                Console.WriteLine("[AgentService] Ollama daemon is active and responsive.");
                return true;
            }
        }

        return false;
    }

    public async Task<bool> IsOllamaHealthyAsync()
    {
        try
        {
            var uri = new Uri($"{_ollamaEndpoint}/api/tags");
            using var response = await _httpClient.GetAsync(uri);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindOllamaExecutable()
    {
        // 1. LocalAppData (standard user install location on Windows)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path1 = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        if (File.Exists(path1)) return path1;

        // 2. Program Files (system-wide install)
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string path2 = Path.Combine(programFiles, "Ollama", "ollama.exe");
        if (File.Exists(path2)) return path2;

        // 3. User profile path
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string path3 = Path.Combine(userProfile, "AppData", "Local", "Programs", "Ollama", "ollama.exe");
        if (File.Exists(path3)) return path3;

        // 4. Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "ollama.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    public async Task<string> ProcessCommandAsync(string userVoiceInput)
    {
        if (string.IsNullOrWhiteSpace(userVoiceInput))
        {
            return "Ready for instructions.";
        }

        // 1. Ensure Ollama service is reachable; auto-start daemon if necessary
        bool isOllamaReady = await EnsureOllamaRunningAsync();

        if (isOllamaReady)
        {
            try
            {
                _history.AddUserMessage(userVoiceInput);

                PromptExecutionSettings settings = new()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                var response = await _chatService.GetChatMessageContentAsync(
                    _history,
                    executionSettings: settings,
                    kernel: _kernel
                );

                string rawContent = response.Content ?? string.Empty;
                string cleanedContent = SanitizeModelOutput(rawContent);

                // 1a. Native Semantic Kernel FunctionCallContent items
                var nativeFunctionCalls = response.Items.OfType<FunctionCallContent>().ToList();
                if (nativeFunctionCalls.Count > 0)
                {
                    foreach (var fc in nativeFunctionCalls)
                    {
                        var kernelArgs = fc.Arguments != null ? new KernelArguments(fc.Arguments) : new KernelArguments();
                        var result = await _kernel.InvokeAsync(fc.PluginName, fc.FunctionName, kernelArgs);
                        string executionOutput = result.GetValue<string>() ?? "Command executed.";
                        _history.AddAssistantMessage(executionOutput);
                        return executionOutput;
                    }
                }

                // 1b. Text-based tool calls emitted by Granite (e.g. [TOOL_CALL: SystemControl-LaunchAndTypeText ...])
                var toolResult = await TryExecuteTextToolCallAsync(cleanedContent, userVoiceInput);
                if (toolResult != null)
                {
                    _history.AddAssistantMessage(toolResult);
                    return toolResult;
                }

                // 1c. Direct intent matching fallback if model responded conversationally instead of calling tool
                var directIntentResult = await TryExecuteDirectIntentAsync(userVoiceInput, cleanedContent);
                if (directIntentResult != null)
                {
                    _history.AddAssistantMessage(directIntentResult);
                    return directIntentResult;
                }

                // 1d. Conversational response
                string finalReply = string.IsNullOrWhiteSpace(cleanedContent) ? "Ready for instructions." : cleanedContent;
                _history.AddAssistantMessage(finalReply);
                return finalReply;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentService] Ollama request error: {ex.Message}");
            }
        }

        // 2. Direct intent fallback if Ollama was unreachable or hit an exception
        var directFallback = await TryExecuteDirectIntentAsync(userVoiceInput, "");
        if (directFallback != null)
        {
            return directFallback;
        }

        // 3. Graceful offline notice if command could not be matched
        return "Local AI brain (Ollama) is currently unreachable on localhost:11434. " +
               "Please ensure Ollama is installed and running ('ollama serve').";
    }

    private string SanitizeModelOutput(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        // Cut off any hallucinatory multiple turns generated by the model
        string[] delimiters = ["\nuser:", "\nUser:", "\nassistant:", "\nAssistant:", "\nUser says:"];
        foreach (var delimiter in delimiters)
        {
            int idx = content.IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                content = content[..idx];
            }
        }

        return content.Trim();
    }

    private async Task<string?> TryExecuteTextToolCallAsync(string text, string originalInput)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Pattern 1: [TOOL_CALL: Plugin-Function {json}] or [SYSTEM_CALL: ...] or [Plugin-Function ...]
        var bracketMatch = Regex.Match(text, @"\[(?:(?:TOOL_CALL|SYSTEM_CALL):\s*)?([A-Za-z0-9_]+)[-.:]([A-Za-z0-9_]+)(?:\s*(\{.*?\}))?\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (bracketMatch.Success)
        {
            string plugin = bracketMatch.Groups[1].Value;
            string function = bracketMatch.Groups[2].Value;
            string jsonArgs = bracketMatch.Groups[3].Value;
            return await InvokeKernelPluginAsync(plugin, function, jsonArgs);
        }

        // Pattern 2: SystemControl-LaunchApplication: {applicationName: 'notepad'}
        var colonMatch = Regex.Match(text, @"([A-Za-z0-9_]+)[-]([A-Za-z0-9_]+):\s*(\{.*?\})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (colonMatch.Success)
        {
            string plugin = colonMatch.Groups[1].Value;
            string function = colonMatch.Groups[2].Value;
            string jsonArgs = colonMatch.Groups[3].Value;
            return await InvokeKernelPluginAsync(plugin, function, jsonArgs);
        }

        // Pattern 3: JSON object { "function": "LaunchApplication", "arguments": { ... } }
        var jsonFuncMatch = Regex.Match(text, @"\{\s*""function""\s*:\s*""([A-Za-z0-9_-]+)""\s*,\s*""arguments""\s*:\s*(\{.*?\})\s*\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (jsonFuncMatch.Success)
        {
            string funcName = jsonFuncMatch.Groups[1].Value;
            string jsonArgs = jsonFuncMatch.Groups[2].Value;
            return await InvokeByNameAsync(funcName, jsonArgs);
        }

        // Pattern 4: Executing SystemControl-LaunchApplication with notepad as the application name
        var executingMatch = Regex.Match(text, @"Executing\s+([A-Za-z0-9_]+)[-]([A-Za-z0-9_]+)\s+with\s+(.+?)\s+as", RegexOptions.IgnoreCase);
        if (executingMatch.Success)
        {
            string plugin = executingMatch.Groups[1].Value;
            string function = executingMatch.Groups[2].Value;
            string val = executingMatch.Groups[3].Value.Trim('\'', '"', ' ');
            return await InvokeKernelPluginAsync(plugin, function, $"{{\"applicationName\": \"{val}\"}}");
        }

        return null;
    }

    private async Task<string?> TryExecuteDirectIntentAsync(string userInput, string modelResponse)
    {
        string trimmed = userInput.Trim();
        string lower = trimmed.ToLowerInvariant();

        // 1. "open <app> and/then/, type/write <text>" or "launch <app> and type <text>"
        var openAndTypeMatch = Regex.Match(trimmed, @"^(?:please\s+)?(?:open|launch|start)\s+([a-zA-Z0-9_\-]+)(?:\s+(?:and|then|,)\s*|\s+)(?:type|write)\s+(.+)$", RegexOptions.IgnoreCase);
        if (openAndTypeMatch.Success)
        {
            string appName = openAndTypeMatch.Groups[1].Value.Trim();
            string textToType = openAndTypeMatch.Groups[2].Value.Trim();
            return await InvokeKernelPluginAsync("SystemControl", "LaunchAndTypeText", $"{{\"applicationName\": \"{appName}\", \"text\": \"{EscapeJson(textToType)}\"}}");
        }

        // 2. "in <app> type/write <text>"
        var inAppTypeMatch = Regex.Match(trimmed, @"^(?:please\s+)?in\s+([a-zA-Z0-9_\-]+)\s+(?:type|write)\s+(.+)$", RegexOptions.IgnoreCase);
        if (inAppTypeMatch.Success)
        {
            string appName = inAppTypeMatch.Groups[1].Value.Trim();
            string textToType = inAppTypeMatch.Groups[2].Value.Trim();
            return await InvokeKernelPluginAsync("SystemControl", "LaunchAndTypeText", $"{{\"applicationName\": \"{appName}\", \"text\": \"{EscapeJson(textToType)}\"}}");
        }

        // 3. "create file <name> with <content>"
        var createFileMatch = Regex.Match(trimmed, @"^(?:please\s+)?create\s+(?:a\s+)?file\s+(?:named\s+|called\s+)?([a-zA-Z0-9_\-\.]+)\s+with\s+(.+)$", RegexOptions.IgnoreCase);
        if (createFileMatch.Success)
        {
            string fileName = createFileMatch.Groups[1].Value.Trim();
            string fileContent = createFileMatch.Groups[2].Value.Trim();
            return await InvokeKernelPluginAsync("SystemControl", "WriteToFileAndOpen", $"{{\"fileName\": \"{fileName}\", \"content\": \"{EscapeJson(fileContent)}\"}}");
        }

        // 4. "type <text>" or "write <text>"
        var typeOnlyMatch = Regex.Match(trimmed, @"^(?:please\s+)?(?:type|write)\s+(.+)$", RegexOptions.IgnoreCase);
        if (typeOnlyMatch.Success && !lower.StartsWith("type into", StringComparison.OrdinalIgnoreCase))
        {
            string textToType = typeOnlyMatch.Groups[1].Value.Trim();
            return await InvokeKernelPluginAsync("SystemControl", "TypeText", $"{{\"text\": \"{EscapeJson(textToType)}\"}}");
        }

        // 5. "open [app]" or "launch [app]"
        var openAppMatch = Regex.Match(lower, @"^(?:please\s+)?(?:open|launch|start|run)\s+([a-zA-Z0-9_\-\. ]+)$");
        if (openAppMatch.Success)
        {
            string appName = openAppMatch.Groups[1].Value.Trim();
            if (appName.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) appName = appName[4..];
            if (appName.Equals("youtube", StringComparison.OrdinalIgnoreCase))
            {
                return await InvokeKernelPluginAsync("WebNavigation", "OpenYouTube", "{}");
            }
            return await InvokeKernelPluginAsync("SystemControl", "LaunchApplication", $"{{\"applicationName\": \"{appName}\"}}");
        }

        // Lock screen / workstation
        if (lower.Contains("lock") && (lower.Contains("screen") || lower.Contains("workstation") || lower.Contains("computer") || lower.Contains("pc")))
        {
            return await InvokeKernelPluginAsync("SystemControl", "LockWorkstation", "{}");
        }

        // Search the web for ...
        var searchMatch = Regex.Match(lower, @"^(?:please\s+)?(?:search\s+(?:the\s+web\s+for|for|google)|google)\s+(.+)$");
        if (searchMatch.Success)
        {
            string query = searchMatch.Groups[1].Value.Trim();
            return await InvokeKernelPluginAsync("WebNavigation", "SearchWeb", $"{{\"query\": \"{query}\"}}");
        }

        // Open YouTube [query]
        var ytMatch = Regex.Match(lower, @"^(?:please\s+)?open\s+youtube(?:\s+(?:and\s+search(?:\s+for)?|for)?\s*(.*))?$");
        if (ytMatch.Success)
        {
            string query = ytMatch.Groups[1].Value.Trim();
            return await InvokeKernelPluginAsync("WebNavigation", "OpenYouTube", $"{{\"query\": \"{query}\"}}");
        }

        // What time is it / current time
        if (lower.Contains("time") && (lower.Contains("what") || lower.Contains("tell") || lower.Contains("current")))
        {
            return await InvokeKernelPluginAsync("SystemControl", "GetCurrentTime", "{}");
        }

        return null;
    }

    private async Task<string?> InvokeByNameAsync(string functionOrPluginName, string jsonArgs)
    {
        if (functionOrPluginName.Contains('-'))
        {
            var parts = functionOrPluginName.Split('-', 2);
            return await InvokeKernelPluginAsync(parts[0], parts[1], jsonArgs);
        }

        // Look through registered plugins
        foreach (var plugin in _kernel.Plugins)
        {
            if (plugin.Contains(functionOrPluginName))
            {
                return await InvokeKernelPluginAsync(plugin.Name, functionOrPluginName, jsonArgs);
            }
        }

        return null;
    }

    private async Task<string> InvokeKernelPluginAsync(string pluginName, string functionName, string? jsonArgs)
    {
        try
        {
            var kernelArgs = new KernelArguments();

            if (!string.IsNullOrWhiteSpace(jsonArgs))
            {
                string normalizedJson = NormalizePseudoJson(jsonArgs);
                try
                {
                    using var doc = JsonDocument.Parse(normalizedJson);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        kernelArgs[prop.Name] = prop.Value.ToString();
                    }
                }
                catch
                {
                    // Fallback regex parameter extraction: {key: 'value'} or {key: "value"}
                    var matches = Regex.Matches(jsonArgs, @"([a-zA-Z0-9_]+)\s*:\s*['""]?([^'""\},]+)['""]?");
                    foreach (Match m in matches)
                    {
                        kernelArgs[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
                    }
                }
            }

            var plugin = _kernel.Plugins[pluginName];
            var function = plugin[functionName];
            var result = await function.InvokeAsync(_kernel, kernelArgs);

            return result.GetValue<string>() ?? $"Executed {functionName}.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin Execution Error]: {ex.Message}");
            return $"Error executing {functionName}: {ex.Message}";
        }
    }

    private static string NormalizePseudoJson(string json)
    {
        string trimmed = json.Trim();
        // Replace single quotes with double quotes
        trimmed = Regex.Replace(trimmed, @"'(.*?)'", @"""$1""");
        // Ensure unquoted keys are quoted: {foo: "bar"} -> {"foo": "bar"}
        trimmed = Regex.Replace(trimmed, @"([{,]\s*)([a-zA-Z0-9_]+)\s*:", @"$1""$2"":");
        return trimmed;
    }

    private static string EscapeJson(string text)
    {
        return text.Replace("\\", "\\\\")
                   .Replace("\"", "\\\"")
                   .Replace("\r", "\\r")
                   .Replace("\n", "\\n");
    }
}
