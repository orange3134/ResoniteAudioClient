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
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> ByteCache = new();

    public static Task<Bitmap?> LoadAsync(string? url)
    {
        if (url == null) return Task.FromResult<Bitmap?>(null);
        return Cache.GetOrAdd(url, static u => FetchAsync(u));
    }

    public static Task<byte[]?> LoadBytesAsync(string? url)
    {
        if (url == null) return Task.FromResult<byte[]?>(null);
        return ByteCache.GetOrAdd(url, static u => FetchBytesAsync(u));
    }

    private static async Task<Bitmap?> FetchAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> FetchBytesAsync(string url)
    {
        try
        {
            return await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
