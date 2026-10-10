using PicView.Core.DebugTools;
using PicView.Core.FileHandling;

namespace PicView.Avalonia.FileSystem;

/// <summary>
/// Records a single label operation (a rename / subfolder move) so it can be undone with
/// Ctrl+Z. Only the most recent label is remembered: applying another label overwrites the
/// record, and performing a deletion clears it. This enforces the rule that a label can only
/// be undone when it was the immediately previous action and no other labeling operation or
/// deletion happened afterwards.
/// </summary>
public sealed class LabelUndoEntry
{
    /// <summary>Path the image had before the label operation (where undo restores it to).</summary>
    public required string ImageOldPath { get; init; }

    /// <summary>Path the image has after the label operation (the current location).</summary>
    public required string ImageNewPath { get; init; }

    /// <summary>Path the companion video had before the label operation, if any.</summary>
    public string? VideoOldPath { get; init; }

    /// <summary>Path the companion video has after the label operation, if any.</summary>
    public string? VideoNewPath { get; init; }
}

/// <summary>
/// Holds the single most-recent undoable label operation. See <see cref="LabelUndoEntry"/>.
/// </summary>
public static class LabelUndoManager
{
    private static readonly object Lock = new();
    private static LabelUndoEntry? _last;

    /// <summary>
    /// Records the most recent label operation, replacing any previous record.
    /// </summary>
    public static void Record(string imageOldPath, string imageNewPath, string? videoOldPath, string? videoNewPath)
    {
        lock (Lock)
        {
            _last = new LabelUndoEntry
            {
                ImageOldPath = imageOldPath,
                ImageNewPath = imageNewPath,
                VideoOldPath = videoOldPath,
                VideoNewPath = videoNewPath
            };
        }
    }

    /// <summary>
    /// Clears the recorded label operation. Called when a deletion happens so a label can no
    /// longer be undone once a later action has occurred.
    /// </summary>
    public static void Clear()
    {
        lock (Lock)
        {
            _last = null;
        }
    }

    /// <summary>
    /// Retrieves and removes the recorded label operation, if any.
    /// </summary>
    public static LabelUndoEntry? PopLast()
    {
        lock (Lock)
        {
            var entry = _last;
            _last = null;
            return entry;
        }
    }

    /// <summary>
    /// Reverses the recorded label operation by moving the image (and any companion video) back
    /// to its original location. Returns true if the image was moved back.
    /// </summary>
    public static bool Undo(LabelUndoEntry entry)
    {
        try
        {
            if (!File.Exists(entry.ImageNewPath))
            {
                return false;
            }

            if (File.Exists(entry.ImageOldPath))
            {
                return false;
            }

            if (!FileHelper.RenameFile(entry.ImageNewPath, entry.ImageOldPath))
            {
                return false;
            }

            // Move the companion video back too, if it moved with the label.
            if (entry.VideoNewPath is not null && entry.VideoOldPath is not null &&
                !string.Equals(entry.VideoNewPath, entry.VideoOldPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(entry.VideoNewPath) && !File.Exists(entry.VideoOldPath))
            {
                FileHelper.RenameFile(entry.VideoNewPath, entry.VideoOldPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(LabelUndoManager), nameof(Undo), ex);
            return false;
        }
    }
}
