using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AudioClient.GUI.Views;

public partial class ResonitePathPromptWindow : Window
{
    public ResonitePathPromptWindow()
    {
        InitializeComponent();
    }

    public async Task<string?> PickFolderAsync(string? initialPath)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = "Select Resonite install directory",
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(initialPath);
            if (folder is not null)
                options.SuggestedStartLocation = folder;
        }

        var result = await StorageProvider.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
}
