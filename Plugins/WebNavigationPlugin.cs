using System.ComponentModel;
using System.Diagnostics;
using Microsoft.SemanticKernel;

namespace JarvisBot.Plugins;

public class WebNavigationPlugin
{
    [KernelFunction, Description("Searches the web using default browser and Google Search")]
    public string SearchWeb([Description("The search query or keywords to look up")] string query)
    {
        try
        {
            string url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return $"Searching the web for '{query}'.";
        }
        catch (Exception ex)
        {
            return $"Failed to search web: {ex.Message}";
        }
    }

    [KernelFunction, Description("Searches or opens YouTube in the default browser")]
    public string OpenYouTube([Description("Search query or video name to search on YouTube, or leave blank to open homepage")] string query = "")
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(query)
                ? "https://www.youtube.com"
                : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return string.IsNullOrWhiteSpace(query) ? "Opening YouTube." : $"Searching YouTube for '{query}'.";
        }
        catch (Exception ex)
        {
            return $"Failed to open YouTube: {ex.Message}";
        }
    }
}
