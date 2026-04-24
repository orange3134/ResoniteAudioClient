using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace AudioClient.GUI.Helpers;

internal static class IconLoader
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();

    public static Task<Bitmap?> LoadAsync(string? url)
    {
        if (url == null) return Task.FromResult<Bitmap?>(null);
        return Cache.GetOrAdd(url, static u => FetchAsync(u));
    }

    private static async Task<Bitmap?> FetchAsync(string url)
    {
        byte[]? bytes = null;
        try
        {
            bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            var bmp = new Bitmap(new MemoryStream(bytes));
            Log($"OK {bytes.Length}b {url}");
            return bmp;
        }
        catch (Exception ex)
        {
            Log($"FAIL({(bytes == null ? "http" : "bitmap")}) {url}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "img_debug.log");
            System.IO.File.AppendAllText(logPath,
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
