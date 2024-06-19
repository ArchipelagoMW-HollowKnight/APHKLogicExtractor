using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RandomizerCore.Json;

namespace APHKLogicExtractor.Loader;

class ResourcePathParser
{
    public readonly string full;
    public readonly string[] parts;
    public readonly string protocol;
    public readonly string path;
    public readonly string? additional;

    public ResourcePathParser(string path)
    {
        this.full = path;
        this.parts = path.Split(":");


        if (this.parts.Length == 2)
        {
            if (this.parts[1].StartsWith("//"))
            {
                this.protocol = this.parts[0];
                this.path = this.parts[1][2..];
            }
            else
            {
                this.protocol = "file";
                this.path = this.parts[0];
                this.additional = this.parts[1];
            }
        }
        else if (this.parts.Length == 3)
        {
            this.protocol = this.parts[0];
            this.path = this.parts[1][2..];
            this.additional = this.parts[2];
        }
        else
        {
            this.protocol = "file";
            this.path = this.parts[0];
        }
    }

    public string Uri { get { return $"{this.protocol}://{this.path}"; } }
}

public class ResourceLoader(ILogger<ResourceLoader> logger)
{
    static readonly string CACHE_DIR = Path.Join(Path.GetTempPath(), "APLogicExtractor");
    static readonly HttpClient client = new();
    private ConcurrentDictionary<string, ResourceLock<byte[]?>> cache = [];

    private async Task<byte[]> GetHttp(string uri)
    {
        // Try to grab cached remote file.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(uri)));
        var cachePath = Path.Join(CACHE_DIR, hash);
        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath);

        // Get the remote file.
        using HttpResponseMessage response = await ResourceLoader.client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();

        // Cache remote file on disk
        logger.LogDebug("Caching content for {uri} on disk at: {cachePath}", uri, cachePath);
        Directory.CreateDirectory(CACHE_DIR);
        await File.WriteAllBytesAsync(cachePath, content);

        return content;
    }

    public async Task<Content> Load(string path)
    {
        // Grab lock on resource.
        var lockable = cache.GetOrAdd(path, new ResourceLock<byte[]?>(null));
        using var guard = await lockable.Enter(TimeSpan.FromSeconds(30));

        // If isn't cached, load it.
        if (guard.Value == null)
        {
            logger.LogDebug("Loading content of: {path}", path);
            var parsed = new ResourcePathParser(path);
            var data = await (parsed switch
            {
                { protocol: "http" } or { protocol: "https" } => this.GetHttp(parsed.Uri),
                _ => File.ReadAllBytesAsync(parsed.path)
            });

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
                    throw new Exception("Unable to decode file content as JSON.");
        }
    }
}
