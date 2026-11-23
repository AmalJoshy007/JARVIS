using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.SemanticKernel;

namespace JarvisBot.Plugins;

public class SystemControlPlugin
{
    #region Win32 API for Keyboard and Window Management
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int SW_RESTORE = 9;

    private static void TypeTextNative(string text)
    {
        foreach (char c in text)
        {
            if (c == '\n')
            {
                SendVirtualKey(0x0D); // VK_RETURN
                continue;
            }
            if (c == '\r') continue;

            var inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE
                    }
                }
            };
            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                    }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(12);
        }
    }

    private static void SendVirtualKey(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } }
        };
        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
        };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        Thread.Sleep(20);
    }
    #endregion

    [KernelFunction, Description("Opens a Windows application like notepad and types the specified text into it")]
    public async Task<string> LaunchAndTypeText(
        [Description("Name or executable of the program, e.g., 'notepad', 'calc', 'chrome'")] string applicationName,
        [Description("The text to type into the application")] string text)
    {
        try
        {
            string cleanApp = applicationName.Trim().Trim('\'', '"', '`').ToLowerInvariant();
            if (cleanApp == "calculator") cleanApp = "calc";

            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = cleanApp,
                    UseShellExecute = true
                });
            }
            catch
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = cleanApp + ".exe",
                    UseShellExecute = true
                });
            }

            // Wait for application window to initialize and receive focus
            await Task.Delay(1000);

            string procName = Path.GetFileNameWithoutExtension(cleanApp);
            bool focused = false;

            // Check started process first
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.WaitForInputIdle(2000);
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(process.MainWindowHandle);
                        focused = true;
                    }
                }
                catch { }
            }

            // If not focused (e.g. Windows 11 Notepad UWP/AppX stub), search by process name
            if (!focused)
            {
                for (int i = 0; i < 10; i++)
                {
                    var matching = Process.GetProcessesByName(procName);
                    var valid = matching.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                    if (valid != null)
                    {
                        ShowWindow(valid.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(valid.MainWindowHandle);
                        focused = true;
                        break;
                    }
                    await Task.Delay(200);
                }
            }

            await Task.Delay(350);

            // Type text into the active window
            TypeTextNative(text);

            return $"Successfully opened {cleanApp} and typed: \"{text}\"";
        }
        catch (Exception ex)
        {
            return $"Failed to launch and type into {applicationName}: {ex.Message}";
        }
    }

    [KernelFunction, Description("Types text into the currently active Windows window")]
    public string TypeText([Description("The text to type")] string text)
    {
        try
        {
            TypeTextNative(text);
            return $"Typed: \"{text}\"";
        }
        catch (Exception ex)
        {
            return $"Failed to type text: {ex.Message}";
        }
    }

    [KernelFunction, Description("Creates a text file on Desktop with the given content and opens it in Notepad")]
    public string WriteToFileAndOpen(
        [Description("Name of the file, e.g., 'tasks.txt', 'notes.txt'")] string fileName,
        [Description("The text content to write into the file")] string content)
    {
        try
        {
            string cleanFile = fileName.Trim().Trim('\'', '"', '`');
            if (!cleanFile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                cleanFile += ".txt";
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fullPath = Path.Combine(desktop, cleanFile);

            File.WriteAllText(fullPath, content);

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = true
            });

            return $"Created '{cleanFile}' on Desktop and opened it in Notepad.";
        }
        catch (Exception ex)
        {
            return $"Failed to write file: {ex.Message}";
        }
    }

    [KernelFunction, Description("Opens a Windows application by name or executable")]
    public string LaunchApplication(
        [Description("Name or executable of the program, e.g., 'notepad', 'chrome', 'calc', 'spotify'")] string applicationName)
    {
        try
        {
            string cleanName = applicationName.Trim().Trim('\'', '"', '`');
            string target = cleanName.ToLowerInvariant() switch
            {
                "calculator" => "calc",
                "file explorer" or "files" => "explorer",
                "command prompt" => "cmd",
                "terminal" => "wt",
                _ => cleanName
            };

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return $"Successfully launched {cleanName}.";
        }
        catch (Exception ex)
        {
            return $"Failed to launch {applicationName}: {ex.Message}";
        }
    }

    [KernelFunction, Description("Locks the Windows workstation screen")]
    public string LockWorkstation()
    {
        try
        {
            Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            return "Workstation locked.";
        }
        catch (Exception ex)
        {
            return $"Failed to lock workstation: {ex.Message}";
        }
    }

    [KernelFunction, Description("Opens a URL in the default web browser")]
    public string OpenWebsite([Description("The full URL to open")] string url)
    {
        try
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return $"Navigating to {url}.";
        }
        catch (Exception ex)
        {
            return $"Failed to open website: {ex.Message}";
        }
    }

    [KernelFunction, Description("Gets current local date and time")]
    public string GetCurrentTime()
    {
        return $"The current local time is {DateTime.Now:h:mm tt} on {DateTime.Now:dddd, MMMM d, yyyy}.";
    }
}
