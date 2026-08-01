using System.Collections.ObjectModel;
using R3;

namespace PicView.Core.FileAssociations;

public class FileTypeGroup : IDisposable
{
    public string Name { get; set; }
    public ObservableCollection<FileTypeItem> FileTypes { get; }

    public BindableReactiveProperty<bool?> IsSelected { get; } = new();

    public FileTypeGroup(string name, IEnumerable<FileTypeItem> fileTypes, bool? isSelected = true)
    {
        Name = name;
        FileTypes = new ObservableCollection<FileTypeItem>(fileTypes);
        IsSelected.Value = isSelected;
    }

    public void Dispose()
    {
        IsSelected.Dispose();
        GC.SuppressFinalize(this);
    }
}