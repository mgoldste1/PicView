using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Input;
using PicView.Core.DebugTools;
using PicView.Core.IPlatform;
using PicView.Core.Keybindings;

namespace PicView.Avalonia.Input;

[JsonSourceGenerationOptions(AllowTrailingCommas = true, WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SourceGenerationContext : JsonSerializerContext;

public static class KeybindingManager
{
    public static Dictionary<KeyGesture, string>? CustomShortcuts { get; private set; }

    /// <summary>
    /// Raised after the keybindings have been persisted, so open UI (such as the
    /// label overview window) can refresh to reflect added/changed shortcuts.
    /// </summary>
    public static event Action? ShortcutsChanged;

    public static async ValueTask LoadKeybindings(IPlatformSpecificService platformSpecificService)
    {
        var keybindings = await KeybindingFunctions.LoadKeyBindingsFile().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(keybindings))
        {
            SetDefaultKeybindings(platformSpecificService);
        }
        else
        {
            UpdateKeybindings(keybindings);
            BackfillMissingDefaultActions(platformSpecificService);
        }
    }

    /// <summary>
    /// Adds default keybindings for any action that is not already bound in the user's saved
    /// keybindings, provided the default gesture is not already in use. This lets newly added
    /// default actions (e.g. UndoDelete, ForceRefresh) reach existing users without discarding
    /// their customizations.
    /// </summary>
    private static void BackfillMissingDefaultActions(IPlatformSpecificService platformSpecificService)
    {
        try
        {
            CustomShortcuts ??= new Dictionary<KeyGesture, string>();

            var defaultJson = platformSpecificService.DefaultJsonKeyMap();
            if (JsonSerializer.Deserialize(
                    defaultJson, typeof(Dictionary<string, string>), SourceGenerationContext.Default)
                is not Dictionary<string, string> defaults)
            {
                return;
            }

            // Actions the user already has bound (to any gesture).
            var boundActions = new HashSet<string>(CustomShortcuts.Values, StringComparer.OrdinalIgnoreCase);

            var added = false;
            foreach (var kvp in defaults)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value) || boundActions.Contains(kvp.Value))
                {
                    continue;
                }

                try
                {
                    var gesture = KeyGesture.Parse(kvp.Key);
                    if (gesture is null)
                    {
                        continue;
                    }

                    // Don't override a gesture the user has already assigned to something else.
                    if (CustomShortcuts.Keys.Any(g => g.Equals(gesture)))
                    {
                        continue;
                    }

                    CustomShortcuts[gesture] = kvp.Value;
                    boundActions.Add(kvp.Value);
                    added = true;
                }
                catch (Exception ex)
                {
                    DebugHelper.LogDebug(nameof(KeybindingManager), nameof(BackfillMissingDefaultActions), ex);
                }
            }

            if (added)
            {
                _ = UpdateKeyBindingsFile();
            }
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(KeybindingManager), nameof(BackfillMissingDefaultActions), ex);
        }
    }

    private static void UpdateKeybindings(string json)
    {
        var keyValues = JsonSerializer.Deserialize(
                json, typeof(Dictionary<string, string>), SourceGenerationContext.Default)
            as Dictionary<string, string>;

        CustomShortcuts ??= new Dictionary<KeyGesture, string>();
        if (keyValues != null)
        {
            PopulateCustomShortcuts(keyValues);
        }
    }

    public static async ValueTask UpdateKeyBindingsFile()
    {
        if (CustomShortcuts == null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(
                CustomShortcuts.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value), 
                typeof(Dictionary<string, string>),
                SourceGenerationContext.Default).Replace("\\u002B", "+"); // Fix plus sign encoded to Unicode
            
            await KeybindingFunctions.SaveKeyBindingsFile(json).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DebugHelper.LogDebug(nameof(KeybindingManager), nameof(UpdateKeyBindingsFile), exception);
        }

        ShortcutsChanged?.Invoke();
    }

    private static void PopulateCustomShortcuts(Dictionary<string, string> keyValues)
    {
        foreach (var kvp in keyValues)
        {
            try
            {
                var gesture = KeyGesture.Parse(kvp.Key);
                if (gesture is not null && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    CustomShortcuts[gesture] = kvp.Value;
                }
            }
            catch (Exception exception)
            {
                DebugHelper.LogDebug(nameof(KeybindingManager), nameof(PopulateCustomShortcuts), exception);
            }
        }
    }

    public static void SetDefaultKeybindings(IPlatformSpecificService platformSpecificService)
    {
        if (CustomShortcuts is not null)
        {
            CustomShortcuts.Clear();
        }
        else
        {
            CustomShortcuts = new Dictionary<KeyGesture, string>();
        }
        
        var defaultKeybindings = platformSpecificService.DefaultJsonKeyMap();

        if (JsonSerializer.Deserialize(
                defaultKeybindings, typeof(Dictionary<string, string>), SourceGenerationContext.Default) 
            is Dictionary<string, string> keyValues)
        {
            PopulateCustomShortcuts(keyValues);
        }
    }
    
    public static string? GetActionName(KeyGesture? keyGesture)
    {
        if (keyGesture is null || CustomShortcuts is null)
        {
            return null;
        }
        
        return CustomShortcuts.GetValueOrDefault(keyGesture);
    }
}