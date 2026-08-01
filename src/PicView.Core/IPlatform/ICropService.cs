namespace PicView.Core.IPlatform;

public interface ICropService
{
    bool IsCropping { get; }
    Task StartCropControlAsync();
    void CloseCropControl();
    
    object? GetCroppedImage();
}
