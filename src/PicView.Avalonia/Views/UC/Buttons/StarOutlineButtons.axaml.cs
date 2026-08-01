using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using PicView.Core.ViewModels;
using R3;

namespace PicView.Avalonia.Views.UC.Buttons;

public partial class StarOutlineButtons : UserControl
{
    private DrawingImage? _filledStar;
    private DrawingImage? _outlinedStar;
    private Image[]? _starIcons;
    
    private IDisposable? _disposable;

    public StarOutlineButtons()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _disposable?.Dispose();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _starIcons = [Star1Icon, Star2Icon, Star3Icon, Star4Icon, Star5Icon];

        if (this.TryFindResource("StarFilledDrawingImage", Application.Current.RequestedThemeVariant, out var filled) &&
            this.TryFindResource("StarOutlineDrawingImage", Application.Current.RequestedThemeVariant,
                out var outlined))
        {
            _filledStar = filled as DrawingImage;
            _outlinedStar = outlined as DrawingImage;
        }

        if (DataContext is not MainWindowViewModel vm || vm.Exif is null)
        {
            SetStars(0); // Ensure stars are outlined if no data context
            return;
        }

        _disposable = Observable.EveryValueChanged(vm.Exif, x => x.ExifRating.Value)
            .Subscribe(SetStars);
    }

    private void SetStars(uint rating)
    {
        if (_starIcons is null || _filledStar is null || _outlinedStar is null)
        {
            return;
        }

        for (var i = 0; i < _starIcons.Length; i++)
        {
            _starIcons[i].Source = i < rating ? _filledStar : _outlinedStar;
        }
    }

    private void UpdateRating(uint newRating)
    {
        if (DataContext is MainWindowViewModel { Exif: not null } vm)
        {
            vm.Exif.ExifRating.Value = newRating;
        }
    }

    private void Stars_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Exif: not null } vm)
        {
            SetStars(vm.Exif.ExifRating.CurrentValue);
        }
        else
        {
            SetStars(0);
        }
    }

    private void Star1_OnPointerEntered(object? sender, PointerEventArgs e) => SetStars(1);
    private void Star2_OnPointerEntered(object? sender, PointerEventArgs e) => SetStars(2);
    private void Star3_OnPointerEntered(object? sender, PointerEventArgs e) => SetStars(3);
    private void Star4_OnPointerEntered(object? sender, PointerEventArgs e) => SetStars(4);
    private void Star5_OnPointerEntered(object? sender, PointerEventArgs e) => SetStars(5);

    private void OneStarCLick(object? sender, RoutedEventArgs e) => UpdateRating(1);
    private void TwoStarCLick(object? sender, RoutedEventArgs e) => UpdateRating(2);
    private void ThreeStarCLick(object? sender, RoutedEventArgs e) => UpdateRating(3);
    private void FourStarCLick(object? sender, RoutedEventArgs e) => UpdateRating(4);
    private void FiveStarCLick(object? sender, RoutedEventArgs e) => UpdateRating(5);
}