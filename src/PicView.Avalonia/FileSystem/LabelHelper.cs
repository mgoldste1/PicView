using ImageMagick;
using PicView.Avalonia.Input;
using PicView.Avalonia.UI;
using PicView.Core.Config;
using PicView.Core.DebugTools;
using PicView.Core.Exif;
using PicView.Core.FileHandling;
using PicView.Core.Navigation;
using PicView.Core.ViewModels;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PicView.Avalonia.FileSystem;

/// <summary>
/// Provides the core labeling logic: apply, replace, toggle-off, and clear labels on image files.
/// </summary>
public static class LabelHelper
{
    private const string LabelPlaceholder = "{LABEL}";
    private const string FilenamePlaceholder = "{FILENAME}";

    /// <summary>
    /// Applies a label to the current image. Handles replace, toggle-off, and advance logic.
    /// </summary>
    public static async Task ApplyLabelAsync(MainWindowViewModel vm, string label)
    {
        var tab = vm.WindowTabs?.ActiveTab?.CurrentValue;
        if (tab?.FileInfo?.CurrentValue is null)
        {
            return;
        }

        var settings = SettingsManager.Settings?.Labeling ?? new LabelSettings();
        var fileInfo = tab.FileInfo.CurrentValue;
        var oldPath = fileInfo.FullName;
        var directory = fileInfo.DirectoryName;
        var originalName = fileInfo.Name;

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (settings.Mode == LabelMode.MoveToSubfolder)
        {
            await ApplyLabelSubfolderMode(vm, tab, settings, label, oldPath, directory, originalName);
        }
        else
        {
            await ApplyLabelFilenameMode(vm, tab, settings, label, oldPath, directory);
        }
    }

    /// <summary>
    /// Removes any known label from the current file without advancing.
    /// </summary>
    public static async Task ClearLabelAsync(MainWindowViewModel vm)
    {
        var tab = vm.WindowTabs?.ActiveTab?.CurrentValue;
        if (tab?.FileInfo?.CurrentValue is null)
        {
            return;
        }

        var settings = SettingsManager.Settings?.Labeling ?? new LabelSettings();
        var fileInfo = tab.FileInfo.CurrentValue;
        var oldPath = fileInfo.FullName;
        var directory = fileInfo.DirectoryName;

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (settings.Mode == LabelMode.MoveToSubfolder)
        {
            // In subfolder mode, clearing means moving the file back to the parent folder
            var parentDir = Path.GetDirectoryName(directory);
            if (parentDir is null)
            {
                return;
            }

            var newPath = Path.Combine(parentDir, fileInfo.Name);
            if (File.Exists(newPath))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot clear label: '{fileInfo.Name}' already exists in parent folder");
                return;
            }

            await PerformRenameAsync(tab, oldPath, newPath, vm);
            TooltipHelper.ShowTooltipMessage("Label cleared");
        }
        else
        {
            // In filename mode, strip any label suffix
            var stem = Path.GetFileNameWithoutExtension(oldPath);
            var ext = Path.GetExtension(oldPath);
            var cleanStem = StripLabelFromStem(stem, settings);

            if (cleanStem == stem)
            {
                // No label found
                return;
            }

            var newName = cleanStem + ext;
            var newPath = Path.Combine(directory, newName);

            if (File.Exists(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot clear label: '{newName}' already exists");
                return;
            }

            await PerformRenameAsync(tab, oldPath, newPath, vm);
            TooltipHelper.ShowTooltipMessage("Label cleared");
        }
    }

    #region Filename Mode

    private static async Task ApplyLabelFilenameMode(
        MainWindowViewModel vm, TabViewModel tab, LabelSettings settings,
        string label, string oldPath, string directory)
    {
        var stem = Path.GetFileNameWithoutExtension(oldPath);
        var ext = Path.GetExtension(oldPath);
        var cleanStem = StripLabelFromStem(stem, settings);
        var currentLabel = GetCurrentLabel(stem, settings);

        // Toggle-off: same label pressed → remove it, stay on current image
        if (string.Equals(currentLabel, label, StringComparison.OrdinalIgnoreCase))
        {
            var newName = cleanStem + ext;
            var newPath = Path.Combine(directory, newName);

            if (File.Exists(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot remove label: '{newName}' already exists");
                return;
            }

            await PerformRenameAsync(tab, oldPath, newPath, vm);
            TooltipHelper.ShowTooltipMessage($"Label '{label}' removed");
            return; // Do NOT advance on toggle-off
        }

        // Check relabel policy if file already has a different label
        if (currentLabel is not null)
        {
            switch (settings.RelabelPolicy)
            {
                case RelabelPolicy.Block:
                    TooltipHelper.ShowTooltipMessage($"File already labeled '{currentLabel}' — relabeling blocked");
                    return;
                case RelabelPolicy.Prompt:
                    // For now, treat as replace since we can't easily show a modal from here without
                    // adding a new dialog system. The prompt behavior is a future enhancement.
                    break;
                case RelabelPolicy.Replace:
                default:
                    break;
            }
        }

        // Apply or replace: build new filename
        var labeledStem = BuildLabeledStem(cleanStem, label, settings);
        var targetName = labeledStem + ext;
        var targetPath = Path.Combine(directory, targetName);

        if (File.Exists(targetPath) && !string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            TooltipHelper.ShowTooltipMessage($"Cannot apply label: '{targetName}' already exists");
            return;
        }

        // Store original filename in metadata if this is the first time labeling
        if (settings.StoreOriginalInMetadata && currentLabel is null)
        {
            await StoreOriginalFilenameAsync(tab.FileInfo.CurrentValue, Path.GetFileName(oldPath));
        }

        await PerformRenameAsync(tab, oldPath, targetPath, vm);
        TooltipHelper.ShowTooltipMessage($"Label: {label}");

        // Advance if configured
        if (settings.AutoAdvanceAfterLabel)
        {
            await vm.WindowTabs.NavigateDirectionalAsync(false, NavigateTo.Next).ConfigureAwait(false);
        }
    }

    #endregion

    #region Subfolder Mode

    private static async Task ApplyLabelSubfolderMode(
        MainWindowViewModel vm, TabViewModel tab, LabelSettings settings,
        string label, string oldPath, string directory, string originalName)
    {
        var currentFolderName = Path.GetFileName(directory);

        // Toggle-off: if the file is already in a folder named after the label, move it back up
        if (string.Equals(currentFolderName, label, StringComparison.OrdinalIgnoreCase))
        {
            var parentDir = Path.GetDirectoryName(directory);
            if (parentDir is null)
            {
                return;
            }

            var newPath = Path.Combine(parentDir, originalName);
            if (File.Exists(newPath))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot remove label: '{originalName}' already exists in parent folder");
                return;
            }

            await PerformRenameAsync(tab, oldPath, newPath, vm);
            TooltipHelper.ShowTooltipMessage($"Label '{label}' removed");
            return; // Do NOT advance on toggle-off
        }

        // Check relabel policy: if file is in a known label subfolder already
        if (IsInLabelSubfolder(directory))
        {
            switch (settings.RelabelPolicy)
            {
                case RelabelPolicy.Block:
                    TooltipHelper.ShowTooltipMessage($"File already in labeled folder '{currentFolderName}' — relabeling blocked");
                    return;
                case RelabelPolicy.Prompt:
                    break;
                case RelabelPolicy.Replace:
                default:
                    break;
            }

            // Move from current label subfolder to new label subfolder
            var parentDir = Path.GetDirectoryName(directory);
            if (parentDir is null)
            {
                return;
            }

            var targetDir = Path.Combine(parentDir, label);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, originalName);

            if (File.Exists(targetPath))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot apply label: '{originalName}' already exists in '{label}' folder");
                return;
            }

            if (settings.StoreOriginalInMetadata)
            {
                await StoreOriginalFilenameAsync(tab.FileInfo.CurrentValue, originalName);
            }

            await PerformRenameAsync(tab, oldPath, targetPath, vm);
        }
        else
        {
            // Move to label subfolder
            var targetDir = Path.Combine(directory, label);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, originalName);

            if (File.Exists(targetPath))
            {
                TooltipHelper.ShowTooltipMessage($"Cannot apply label: '{originalName}' already exists in '{label}' folder");
                return;
            }

            if (settings.StoreOriginalInMetadata)
            {
                await StoreOriginalFilenameAsync(tab.FileInfo.CurrentValue, originalName);
            }

            await PerformRenameAsync(tab, oldPath, targetPath, vm);
        }

        TooltipHelper.ShowTooltipMessage($"Label: {label}");

        if (settings.AutoAdvanceAfterLabel)
        {
            await vm.WindowTabs.NavigateDirectionalAsync(false, NavigateTo.Next).ConfigureAwait(false);
        }
    }

    private static bool IsInLabelSubfolder(string directory)
    {
        // The file is considered to be inside a label subfolder only when the current
        // folder name matches one of the user's configured label categories. Using a
        // broad heuristic here previously caused ordinary folders (e.g. "Pictures") to
        // be treated as label folders, so relabeling created a sibling folder in the
        // parent instead of a subfolder.
        var folderName = Path.GetFileName(directory);
        if (string.IsNullOrEmpty(folderName))
        {
            return false;
        }

        return GetKnownLabels().Contains(folderName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the set of configured label names (the text after "Label:" in the keybindings).
    /// </summary>
    private static IEnumerable<string> GetKnownLabels()
    {
        var shortcuts = KeybindingManager.CustomShortcuts;
        if (shortcuts is null)
        {
            return [];
        }

        const string labelPrefix = "Label:";
        return shortcuts.Values
            .Where(v => v.StartsWith(labelPrefix, StringComparison.Ordinal))
            .Select(v => v[labelPrefix.Length..]);
    }

    #endregion

    #region Stem Manipulation

    /// <summary>
    /// Gets the current label from a filename stem, or null if none found.
    /// </summary>
    internal static string? GetCurrentLabel(string stem, LabelSettings settings)
    {
        var separator = settings.LabelSeparator;

        if (IsFullTemplate(separator))
        {
            var regex = BuildTemplateRegex(NormalizeTemplate(separator));
            if (regex is null)
            {
                return null;
            }

            var match = regex.Match(stem);
            return match.Success ? match.Groups["label"].Value : null;
        }

        if (separator.Contains(LabelPlaceholder, StringComparison.Ordinal))
        {
            // Template mode: e.g. "_({LABEL})" → prefix="_(" suffix=")"
            var placeholderIndex = separator.IndexOf(LabelPlaceholder, StringComparison.Ordinal);
            var prefix = separator[..placeholderIndex];
            var suffix = separator[(placeholderIndex + LabelPlaceholder.Length)..];

            // Find the last occurrence of prefix...suffix pattern
            var prefixPos = stem.LastIndexOf(prefix, StringComparison.Ordinal);
            if (prefixPos < 0)
            {
                return null;
            }

            var labelStart = prefixPos + prefix.Length;
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixPos = stem.IndexOf(suffix, labelStart, StringComparison.Ordinal);
                if (suffixPos < 0 || suffixPos + suffix.Length != stem.Length)
                {
                    return null;
                }

                return stem[labelStart..suffixPos];
            }

            return stem[labelStart..];
        }
        else
        {
            // Simple separator mode: last occurrence of separator
            var sepPos = stem.LastIndexOf(separator, StringComparison.Ordinal);
            if (sepPos < 0 || sepPos == 0)
            {
                return null;
            }

            return stem[(sepPos + separator.Length)..];
        }
    }

    /// <summary>
    /// Strips the label suffix (separator + label) from the stem.
    /// </summary>
    internal static string StripLabelFromStem(string stem, LabelSettings settings)
    {
        var separator = settings.LabelSeparator;

        if (IsFullTemplate(separator))
        {
            var normalized = NormalizeTemplate(separator);
            var regex = BuildTemplateRegex(normalized);
            if (regex is null)
            {
                return stem;
            }

            var match = regex.Match(stem);
            return match.Success ? match.Groups["filename"].Value : stem;
        }

        if (separator.Contains(LabelPlaceholder, StringComparison.Ordinal))
        {
            var placeholderIndex = separator.IndexOf(LabelPlaceholder, StringComparison.Ordinal);
            var prefix = separator[..placeholderIndex];
            var suffix = separator[(placeholderIndex + LabelPlaceholder.Length)..];

            var prefixPos = stem.LastIndexOf(prefix, StringComparison.Ordinal);
            if (prefixPos < 0)
            {
                return stem;
            }

            if (!string.IsNullOrEmpty(suffix))
            {
                var labelStart = prefixPos + prefix.Length;
                var suffixPos = stem.IndexOf(suffix, labelStart, StringComparison.Ordinal);
                if (suffixPos < 0 || suffixPos + suffix.Length != stem.Length)
                {
                    return stem;
                }
            }

            return stem[..prefixPos];
        }
        else
        {
            var sepPos = stem.LastIndexOf(separator, StringComparison.Ordinal);
            if (sepPos <= 0)
            {
                return stem;
            }

            return stem[..sepPos];
        }
    }

    /// <summary>
    /// Builds the labeled stem using the configured separator pattern.
    /// </summary>
    private static string BuildLabeledStem(string cleanStem, string label, LabelSettings settings)
    {
        var separator = settings.LabelSeparator;
        var lowerLabel = label.ToLowerInvariant();

        if (IsFullTemplate(separator))
        {
            var normalized = NormalizeTemplate(separator);
            return normalized
                .Replace(FilenamePlaceholder, cleanStem, StringComparison.Ordinal)
                .Replace(LabelPlaceholder, lowerLabel, StringComparison.Ordinal);
        }

        if (separator.Contains(LabelPlaceholder, StringComparison.Ordinal))
        {
            var expanded = separator.Replace(LabelPlaceholder, lowerLabel, StringComparison.Ordinal);
            return cleanStem + expanded;
        }

        return cleanStem + separator + lowerLabel;
    }

    #endregion

    #region Template parsing

    /// <summary>
    /// Determines whether the separator is a full-filename template, i.e. it contains
    /// the {FILENAME} placeholder (in addition to a label placeholder). Accepts both
    /// "{FILENAME}" and "${FILENAME}" forms, case-insensitively.
    /// </summary>
    private static bool IsFullTemplate(string separator)
    {
        return separator.Contains("FILENAME", StringComparison.OrdinalIgnoreCase) &&
               (separator.Contains("LABEL", StringComparison.OrdinalIgnoreCase) ||
                separator.Contains("CATEGORY", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes a user template into a canonical form that uses {FILENAME} and {LABEL}
    /// tokens. Supports ${...} and {...} syntax and the CATEGORY alias for LABEL,
    /// case-insensitively.
    /// </summary>
    private static string NormalizeTemplate(string template)
    {
        // Convert ${TOKEN} and {TOKEN} (any casing, with optional $) to canonical tokens.
        return Regex.Replace(
            template,
            @"\$?\{\s*(?<name>FILENAME|LABEL|CATEGORY)\s*\}",
            m =>
            {
                var name = m.Groups["name"].Value.ToUpperInvariant();
                return name == "FILENAME" ? FilenamePlaceholder : LabelPlaceholder;
            },
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Builds a regex from a normalized template (containing {FILENAME} and {LABEL})
    /// that can extract the original filename and label back out of a stem.
    /// Returns null if the template is missing either placeholder.
    /// </summary>
    private static Regex? BuildTemplateRegex(string normalizedTemplate)
    {
        var filenameIndex = normalizedTemplate.IndexOf(FilenamePlaceholder, StringComparison.Ordinal);
        var labelIndex = normalizedTemplate.IndexOf(LabelPlaceholder, StringComparison.Ordinal);
        if (filenameIndex < 0 || labelIndex < 0)
        {
            return null;
        }

        var pattern = new StringBuilder("^");
        var i = 0;
        while (i < normalizedTemplate.Length)
        {
            if (string.CompareOrdinal(normalizedTemplate, i, FilenamePlaceholder, 0, FilenamePlaceholder.Length) == 0)
            {
                // Filename can contain anything; capture non-greedily so trailing
                // literals/label still match.
                pattern.Append("(?<filename>.+?)");
                i += FilenamePlaceholder.Length;
            }
            else if (string.CompareOrdinal(normalizedTemplate, i, LabelPlaceholder, 0, LabelPlaceholder.Length) == 0)
            {
                // Label has no separator/whitespace of its own.
                pattern.Append("(?<label>[^\\s]+?)");
                i += LabelPlaceholder.Length;
            }
            else
            {
                pattern.Append(Regex.Escape(normalizedTemplate[i].ToString()));
                i++;
            }
        }

        pattern.Append('$');

        try
        {
            return new Regex(pattern.ToString(), RegexOptions.CultureInvariant);
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(LabelHelper), nameof(BuildTemplateRegex), ex);
            return null;
        }
    }

    #endregion

    #region Helpers

    private static Task PerformRenameAsync(TabViewModel tab, string oldPath, string newPath, MainWindowViewModel vm)
    {
        try
        {
            if (!FileHelper.RenameFile(oldPath, newPath))
            {
                TooltipHelper.ShowTooltipMessage("Rename failed");
                return Task.CompletedTask;
            }

            // Update the model's FileInfo (title/path) but keep the already-loaded image alive.
            // The file-list, gallery and current index are reconciled by FileWatcherService's
            // Renamed handler. We intentionally do NOT clear the cache / reload here: doing so
            // disposes the currently displayed bitmap and races with rendering (ObjectDisposedException).
            var newFileInfo = new FileInfo(newPath);
            tab.FileInfo.Value = newFileInfo;
            tab.Model.FileInfo = newFileInfo;
            tab.UpdateTabTitle();
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(LabelHelper), nameof(PerformRenameAsync), ex);
            TooltipHelper.ShowTooltipMessage($"Error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static async Task StoreOriginalFilenameAsync(FileInfo fileInfo, string originalFileName)
    {
        try
        {
            await ExifFunctions.TryUpdateImageProfileAsync(fileInfo, magickImage =>
            {
                var profile = magickImage.GetExifProfile() ?? new ExifProfile();
                // Use ImageDescription to store the original filename
                profile.SetValue(ExifTag.ImageDescription, $"OriginalName:{originalFileName}");
                magickImage.SetProfile(profile);
                return true;
            }, nameof(LabelHelper), nameof(StoreOriginalFilenameAsync));
        }
        catch (Exception ex)
        {
            // Non-fatal: if metadata write fails (e.g. PNG), just continue
            DebugHelper.LogDebug(nameof(LabelHelper), nameof(StoreOriginalFilenameAsync), ex);
        }
    }

    #endregion
}
