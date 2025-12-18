using Ivory.Application.Laravel;
using Ivory.Application.Php;

namespace Ivory.Infrastructure.Laravel;

public class LaravelService(IPhpRuntimeService runtime) : ILaravelService
{
    private const string DownloadUrl = "https://download.herdphp.com/resources/laravel";
    private readonly IPhpRuntimeService _runtime = runtime;

    public async Task<int> RunLaravelAsync(string[] args, string phpVersionSpec, CancellationToken cancellationToken = default)
    {
        string laravelPath;
        try
        {
            laravelPath = await EnsureLaravelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ivory] Failed to prepare Laravel installer: {ex.Message}");
            return 1;
        }

        string[] forwardedArgs = args ?? Array.Empty<string>();
        return await _runtime.RunPhpAsync(laravelPath, forwardedArgs, phpVersionSpec, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> EnsureLaravelAsync(CancellationToken cancellationToken)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string ivoryDir = Path.Combine(home, ".ivory");
        Directory.CreateDirectory(ivoryDir);

        string targetPath = Path.Combine(ivoryDir, "laravel");
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        Console.WriteLine($"[ivory] Downloading Laravel installer from {DownloadUrl} ...");

        using var client = new HttpClient();
        using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var destination = File.Create(targetPath))
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"[ivory] Saved Laravel installer to {targetPath}");

        return targetPath;
    }
}
