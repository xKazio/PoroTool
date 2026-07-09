using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// Downloads CDN images and hands out downscaled thumbnails. The cache
    /// owns every Image it returns - controls only borrow them and must never
    /// dispose them. Champion icons are small and kept for the session; splash
    /// thumbnails are larger and cleared when a grid is torn down. Full-size
    /// downloads are decoded off the UI thread and disposed immediately.
    /// </summary>
    static class ImageCache
    {
        private static readonly HttpClient http = new HttpClient();
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(8);
        private static readonly object sync = new object();

        private static readonly Dictionary<string, Image> icons = new Dictionary<string, Image>();
        private static readonly Dictionary<string, Image> splashes = new Dictionary<string, Image>();

        public static Task<Image> GetIconAsync(string url, int width, int height, CancellationToken token)
        {
            return GetAsync(icons, url, width, height, token);
        }

        public static Task<Image> GetSplashAsync(string url, int width, int height, CancellationToken token)
        {
            return GetAsync(splashes, url, width, height, token);
        }

        /// <summary>
        /// Only call after every control showing a splash has been disposed;
        /// in-flight loads are expected to be cancelled first, and their
        /// callers must re-check their token before using a returned image.
        /// </summary>
        public static void ClearSplashCache()
        {
            lock (sync)
            {
                foreach (var image in splashes.Values)
                    image.Dispose();
                splashes.Clear();
            }
        }

        private static async Task<Image> GetAsync(Dictionary<string, Image> cache, string url, int width, int height, CancellationToken token)
        {
            lock (sync)
            {
                if (cache.TryGetValue(url, out var cached)) return cached;
            }

            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (cache.TryGetValue(url, out var cached)) return cached;
                }

                byte[] bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                Image thumbnail = MakeThumbnail(bytes, width, height);
                lock (sync)
                {
                    if (cache.TryGetValue(url, out var raced))
                    {
                        thumbnail.Dispose();
                        return raced;
                    }
                    cache[url] = thumbnail;
                }
                return thumbnail;
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 404s, HTML error pages, corrupt bytes: leave the placeholder.
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private static Image MakeThumbnail(byte[] bytes, int width, int height)
        {
            using (var stream = new MemoryStream(bytes))
            using (var full = Image.FromStream(stream))
            {
                var thumbnail = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(full, 0, 0, width, height);
                }
                return thumbnail;
            }
        }
    }
}
