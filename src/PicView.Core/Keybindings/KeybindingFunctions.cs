using PicView.Core.Config.ConfigFileManagement;
using PicView.Core.FileHandling;

namespace PicView.Core.Keybindings;

public static class KeybindingFunctions
{
    public static KeyBindingsConfiguration? KeyBindingsConfiguration { get; private set; }
    public static string CurrentKeybindingsPath => KeyBindingsConfiguration?.CorrectPath ?? string.Empty;

    public static async Task SaveKeyBindingsFile(string json)
    {
        try
        {
            KeyBindingsConfiguration ??= new KeyBindingsConfiguration();
            if (string.IsNullOrEmpty(KeyBindingsConfiguration.CorrectPath))
            {
                KeyBindingsConfiguration.CorrectPath = ConfigFileManager.ResolveDefaultConfigPath(KeyBindingsConfiguration);
            }
            FileHelper.EnsureDirectoryExists(CurrentKeybindingsPath);
            await using var writer = new StreamWriter(CurrentKeybindingsPath);
            await writer.WriteAsync(json).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            FileHelper.EnsureDirectoryExists(KeyBindingsConfiguration.RoamingConfigPath);
            await using var writer = new StreamWriter(KeyBindingsConfiguration.RoamingConfigPath);
            await writer.WriteAsync(json).ConfigureAwait(false);
        }
    }

    public static async Task<string?> LoadKeyBindingsFile()
    {
        var path = ConfigFileManager.ResolveDefaultConfigPath(KeyBindingsConfiguration ??= new KeyBindingsConfiguration());
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        
        KeyBindingsConfiguration.CorrectPath = path;
        return text;
    }
}