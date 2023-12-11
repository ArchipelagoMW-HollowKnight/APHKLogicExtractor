using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Web;

namespace APHKLogicExtractor.Loaders
{
    internal class BaseLoader
    {
        static readonly HttpClient client = new();

        private string encodedRefName;
        private ConcurrentDictionary<string, SemaphoreSlim> keywiseLocks = new();
        private Dictionary<string, object> cache = new();

        public BaseLoader(string refName)
        {
            this.encodedRefName = HttpUtility.UrlEncode(refName);
        }

        private string FormatUrl(string relativePath) => $"https://raw.githubusercontent.com/homothetyhk/RandomizerMod/{encodedRefName}/{relativePath}";

        private async Task<T> LoadJsonData<T>(string relativePath) where T : class
        {
            string url = FormatUrl(relativePath);
            using HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(content) ?? throw new NullReferenceException();
        }

        protected async Task<T> LoadJsonCached<T>(string relativePath) where T : class
        {
            SemaphoreSlim lockable = keywiseLocks.GetOrAdd(relativePath, new SemaphoreSlim(1));
            await lockable.WaitAsync(TimeSpan.FromSeconds(30));
            try
            {
                if (cache.TryGetValue(relativePath, out object? obj) && obj is T tt)
                {
                    return tt;
                }
                tt = await LoadJsonData<T>(relativePath);
                cache[relativePath] = tt;
                return tt;
            }
            finally
            {
                lockable.Release();
            }
        }
    }
}
