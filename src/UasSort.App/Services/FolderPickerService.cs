// src/UasSort.App/Services/FolderPickerService.cs — Windows App SDK picker; returns a path string (no StorageFolder: banned)
using Microsoft.Windows.Storage.Pickers;

namespace UasSort.App.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync(MainWindow window, string? startFolder)
    {
        var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        if (!string.IsNullOrEmpty(startFolder)) picker.SuggestedStartFolder = startFolder;
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }
}
