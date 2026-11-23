using Microsoft.Extensions.Configuration;

namespace JarvisBot.Configuration;

public class AppSettings
{
    public JarvisConfig Jarvis { get; set; } = new();

    public static AppSettings Load()
    {
        var basePath = AppContext.BaseDirectory;
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        var config = builder.Build();
        var settings = new AppSettings();
        config.Bind(settings);

        return settings;
    }
}

public class JarvisConfig
{
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string ModelId { get; set; } = "granite3.1-dense:2b";
    public float SpeechConfidenceThreshold { get; set; } = 0.30f;
    public int SpeechRate { get; set; } = 1;
    public string SystemPrompt { get; set; } =
        "You are J.A.R.V.I.S., a direct, concise, and efficient desktop AI assistant. " +
        "When the user requests an action, use the tool syntax: [TOOL_CALL: PluginName-FunctionName {\"param\": \"value\"}]. " +
        "Available tools: SystemControl-LaunchAndTypeText, SystemControl-TypeText, SystemControl-WriteToFileAndOpen, " +
        "SystemControl-LaunchApplication, SystemControl-LockWorkstation, SystemControl-OpenWebsite, " +
        "SystemControl-GetCurrentTime, WebNavigation-SearchWeb, WebNavigation-OpenYouTube. " +
        "If no tool is needed, reply directly in under 2 sentences.";
}
