# PicView – Copilot Instructions

## Solution layout

The solution (`PicViewLabelizer.slnx`) lives at the repo root; the actual source is under `PicView/src/`.

Key projects:

- **PicView.Core** – Platform-agnostic core logic (navigation, view models, config, file history). No Avalonia/UI dependencies.
- **PicView.Avalonia** – Shared Avalonia UI layer (views, helpers, file-system operations, window behavior).
- **PicView.Avalonia.Win32 / .Linux / .MacOS** – Platform-specific window providers and entry points (`Program.Main`).
- **PicView.Tests** – xUnit tests, including per-language translation tests under `LanguageTests/`.
- **PicView.Benchmarks** – BenchmarkDotNet performance projects.

## Where things live

- **Navigation & file watching**: `PicView.Core/Navigation/` (e.g. `NavigationService.cs`, `FileWatcherService.cs`). `NavigationService.LoadLastFileAsync` resolves the "last file" from `Settings.StartUp.LastFile`, falling back to `FileHistoryManager.GetLastEntry()`.
- **View models**: `PicView.Core/ViewModels/` (e.g. `TabViewModel`, `GalleryViewModel`, `TabOverviewViewModel`, `ToolTipViewModel`, `TranslationViewModel`). Reactive properties use `BindableReactiveProperty<T>` / `ReactiveProperty<T>` (R3).
- **Gallery**: `GalleryViewModel.GalleryItems` is an `ObservableList<GalleryItemViewModel>`. The `files` list (`ImageIterator.Files`, a `List<FileInfo>`) and `GalleryItems` are separate collections and are NOT guaranteed to share indices — match gallery items by `FileInfo.FullName`, not by a `files` index.
- **Config / settings**: `PicView.Core.Config` namespace; access via `Settings.*` (e.g. `Settings.StartUp.LastFile`, `Settings.Navigation.*`, `Settings.Gallery.*`).
- **File history**: `PicView.Core.FileHistory` (`FileHistoryManager`).
- **UI helpers**: `PicView.Avalonia/UI/` (e.g. `UIHelper.cs`, `TooltipHelper.cs`). `UIHelper.OpenLastFile` handles the "open last image" startup action.
- **Tooltips / toasts**: `TooltipHelper.ShowTooltipMessage(object message, bool center = false, TimeSpan? interval = null)` shows the standard toast. Pass a `TimeSpan` to control how long it stays. Backed by `ToolTipViewModel` (`ToolTipMessageSource`, `ToolTipMessageCentered`, `ToolTipMessageInterval`) and the `ToolTipMessage` user control.
- **Labels & file operations**: `PicView.Avalonia/FileSystem/` (e.g. `LabelHelper.cs`, `FileManager.cs`).
- **Translations**: `TranslationManager.Translation.*`; each language has a matching test in `PicView.Tests/LanguageTests/`.

## Image cache & preloading pipeline

- **Central cache**: `PicView.Core/Navigation/SharedImageCache.cs` implements `IImageCache` and coordinates the `Preloader2` worker. It stores `PreLoadValue`s per-owner (tab) in `EvictingDictionary<PreLoadValue>` plus a fast `_pathLookup` keyed by `FileInfo.FullName` (OS-specific comparer).
- **Lazy disposal / resurrection**: When an item is no longer referenced (`CheckAndDisposeIfNotReferenced`), it is NOT disposed immediately — it is moved to a 30-second `_disposalList` queue and its underlying image is disposed later. `TryGet`/`TryResurrect` can pull an entry back out of that queue. Beware: a resurrected `PreLoadValue` may have a **disposed** (non-null) `ImageModel.Image`, which renders blank and throws `ObjectDisposedException` in `PicBox` (`GetImageSize`/`RenderImageSource`). Null-checking `Image` is not enough; the object can be a disposed `Bitmap`.
- **File-move reconciliation**: `FileWatcherService` (`OnFileCreatedAsync`/`OnFileDeletedAsync`/`OnFileRenamedAsync`) updates `ImageIterator.Files`, gallery items, title count, then calls `_cache.Resynchronize(tab.Id, files)`. When a file re-enters a watched folder (e.g. a labeled file moved back from a subfolder), call `IImageCache.EvictPath(fullPath)` first so any stale/disposed cached entry is fully removed (owner dicts, `_pathLookup`, and disposal queue) and navigation forces a fresh disk load instead of resurrecting a dead bitmap.
- **Navigation render path**: `ImageIterator.IterateToIndexAsync` decides between using a cached `ImageModel` (`IsLoading == false && Image != null`), waiting on an in-flight load, or loading fresh via `Cache.LoadAsync` → `Preloader2.AddAsync`. Treat a `{ IsLoading: false, ImageModel.Image: null }` cached value as a miss and reload.

## Labeling subsystem (Labelizer fork)

- **Core logic**: `PicView.Avalonia/FileSystem/LabelHelper.cs` implements apply/clear/rename plus filename-append and subfolder label modes, template tokens (`{FILENAME}`, `{LABEL}`, `{CATEGORY}`, also `${...}` form), original-name EXIF storage, non-overwrite safeguards, and optional auto-advance.
- **Config**: `LabelSettings` in `PicView.Core/Config/AppSettings.cs` (`AutoAdvanceAfterLabel`, `RelabelPolicy`, `LabelSeparator`, `StoreOriginalInMetadata`, `Mode`); defaulted/backfilled in `SettingsManager`.
- **Keybindings as label source of truth**: labels are stored as dynamic action names (`Label:<name>`) in `KeybindingManager.CustomShortcuts` (`Dictionary<KeyGesture, string>`). `FunctionsMapper.GetDynamicFunction` dispatches them. Subfolder detection keys off actual configured label categories (not path shape). Persist edits via `KeybindingManager.UpdateKeyBindingsFile()`.
- **Settings UI**: `Views/Config/LabelingView.axaml(.cs)` (inline category editor) wired through `SettingsViewModel` (`SettingsCategory.Labeling`) and registered in `Views/Main/SettingsView.axaml`.
- **Config persistence**: config lives in roaming AppData (`%APPDATA%\PicView\Config\...` via `ConfigFile.ConfigFolder`). Ensure target directories exist before writing, and don't let startup logic reset saved keybindings to defaults.


## Conventions

- Target framework: .NET 10.
- Prefer existing helpers (`TooltipHelper`, `FileHistoryManager`, `Settings`) over re-implementing.
- UI work must marshal to the UI thread via `Dispatcher.UIThread.InvokeAsync`.
- Views live in `PicView.Avalonia/Views/` (`Main/`, `UC/` for user controls).