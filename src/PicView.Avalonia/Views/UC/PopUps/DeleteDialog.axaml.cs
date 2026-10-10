using Avalonia;
using PicView.Avalonia.CustomControls;
using PicView.Avalonia.UI;
using PicView.Core.Localization;
using PicView.Core.ViewModels;

namespace PicView.Avalonia.Views.UC.PopUps;

public partial class DeleteDialog : AnimatedPopUp
{
    public DeleteDialog(string prompt, string file, bool recycle)
    {
        InitializeComponent();
        ConfirmButtonText.Text = recycle
            ? TranslationManager.Translation.MoveToRecycleBin
            : TranslationManager.Translation.DeleteFile;

        Loaded += delegate
        {
            if (Application.Current.DataContext is not CoreViewModel core)
            {
                return;
            }

            PromptText.Text = prompt;
            PromptFileName.Text = Path.GetFileName(file) + "?";
            CancelButton.Click += async delegate { await AnimatedClosing(); };
            ConfirmButton.Click += async delegate
            {
                FileSystem.DeleteManager.MoveCompanionVideoAndRecord(file, recycle);
                var success = await core.PlatformService.DeleteFile(file, recycle);
                if (success)
                {
                    var msg = recycle
                        ? TranslationManager.Translation.SentFileToRecycleBin
                        : TranslationManager.Translation.DeletedFile;
                    TooltipHelper.ShowTooltipMessage(msg + Environment.NewLine + Path.GetFileName(file));
                }
                else if (File.Exists(file))
                {
                    TooltipHelper.ShowTooltipMessage(TranslationManager.Translation.UnexpectedError, true);
                }
                await AnimatedClosing();
            };

            Focus();
        };
    }
}