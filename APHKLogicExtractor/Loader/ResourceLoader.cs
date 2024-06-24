using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RandomizerCore.Json;

namespace APHKLogicExtractor.Loader;

internal class ResourceLoader(IOptions<CommandLineOptions> options, ILogger<ResourceLoader> logger)
{
    static readonly string CACHE_DIR = Path.Join(Path.GetTempPath(), "APLogicExtractor");
    static readonly HttpClient client = new();
    private ConcurrentDictionary<string, ResourceLock<byte[]?>> cache = [];

    private async Task<byte[]> GetHttp(string uri)
    {
        // Try to grab cached remote file.
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(uri)));
        string cachePath = Path.Join(CACHE_DIR, hash);
        if (!options.Value.IgnoreCache && File.Exists(cachePath))
        {
            logger.LogInformation("Loading {uri} from cache ({cachePath})", uri, cachePath);
            return await File.ReadAllBytesAsync(cachePath);
        }

        // Get the remote file.
        using HttpResponseMessage response = await ResourceLoader.client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        byte[] content = await response.Content.ReadAsByteArrayAsync();

        // Cache remote file on disk
        logger.LogInformation("Caching content for {uri} on disk at: {cachePath}", uri, cachePath);
        Directory.CreateDirectory(CACHE_DIR);
        await File.WriteAllBytesAsync(cachePath, content);

        return content;
    }

    public async Task<Content> Load(string path)
    {
        // Grab lock on resource.
        ResourceLock<byte[]?> lockable = cache.GetOrAdd(path, new ResourceLock<byte[]?>(null));
        using ResourceLock<byte[]?>.LockGuard guard = await lockable.Enter(TimeSpan.FromSeconds(30));

        // If isn't cached, load it.
        if (guard.Value == null)
        {
            logger.LogInformation("Loading content of: {path}", path);
            byte[] data = [];

            string[] split = path.Split("://");
            if (split.Length == 2)
            {
                if (split[0].StartsWith("http")) data = await this.GetHttp(path);
                else if (split[0] == "file") data = await File.ReadAllBytesAsync(split[1]);
                else throw new InvalidOperationException("Unsupported resource protocol");
            }
            else
            {
                data = await File.ReadAllBytesAsync(path);
            }

            guard.Value = data;
        }

        return new Content(this, guard.Value);
    }

    public class Content(ResourceLoader resourceLoader, byte[] content)
    {
        /// <summary>
        /// Get the resource content as a string.
        /// </summary>
        /// <returns>The content as a string.</returns>
        public string AsString()
        {
            return Encoding.UTF8.GetString(content);
        }

        /// <summary>
        /// Get the resourse content as a data stream.
        /// </summary>
        /// <returns>The content as a data stream.</returns>
        public Stream AsStream()
        {
            return new MemoryStream(content);
        }

        /// <summary>
        /// Parse the content as JSON object of a given type.
        /// </summary>
        /// <typeparam name="T">The type of the JSON object.</typeparam>
        /// <returns>The content as a JSON object.</returns>
        /// <exception cref="Exception">Throws when failing to parse the content.</exception>
        public T AsJson<T>() where T : class
        {
            return JsonUtils
                .GetSerializer(resourceLoader)
                .DeserializeFromStream<T>(this.AsStream()) ??
                    throw new JsonSerializationException("Unable to decode file content as JSON.");
        }
    }
}
