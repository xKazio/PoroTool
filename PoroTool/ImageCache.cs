using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// Downloads CDN images and hands out downscaled thumbnails. Three tiers:
    /// an in-memory cache (instant), a disk cache under LocalAppData (survives
    /// restarts, so icons only ever download once), and the network. The cache
    /// owns every Image it returns - controls only borrow them and must never
    /// dispose them. Champion icons are kept for the session; splash thumbnails
    /// are cleared from memory when a grid is torn down (they stay on disk).
    /// </summary>
    static class ImageCache
    {
        private static readonly HttpClient http = new HttpClient();
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(16);
        private static readonly object sync = new object();

        private static readonly Dictionary<string, Image> icons = new Dictionary<string, Image>();
        private static readonly Dictionary<string, Image> splashes = new Dictionary<string, Image>();

        private static readonly string cacheDir;
        private static readonly bool diskEnabled;

        static ImageCache()
        {
            try
            {
                cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PoroTool", "imgcache");
                Directory.CreateDirectory(cacheDir);
                diskEnabled = true;
            }
            catch
            {
                diskEnabled = false;
            }
        }

        public static Task<Image> GetIconAsync(string url, int width, int height, CancellationToken token)
        {
            return GetAsync(icons, url, width, height, token);
        }

        public static Task<Image> GetSplashAsync(string url, int width, int height, CancellationToken token)
        {
            return GetAsync(splashes, url, width, height, token);
        }

        /// <summary>
        /// Drops splash thumbnails from memory (they remain on disk). Only call
        /// after every control showing a splash has been disposed and in-flight
        /// loads are cancelled.
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

            string diskPath = diskEnabled ? DiskPath(url, width, height) : null;

            // Disk read + decode on a worker thread so a cache full of icons
            // never blocks the UI thread, and without holding the network gate.
            if (diskPath != null)
            {
                var fromDisk = await Task.Run(() => LoadFromDisk(cache, url, diskPath, token)).ConfigureAwait(false);
                if (fromDisk != null) return fromDisk;
            }

            if (token.IsCancellationRequested) return null;

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
                // Persist before sharing the bitmap: at this point no control
                // references it, so Save() can read it without a threading race.
                if (diskPath != null) SaveToDisk(diskPath, thumbnail);

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
            catch (OperationCanceledException)
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

        private static Image LoadFromDisk(Dictionary<string, Image> cache, string url, string path, CancellationToken token)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                if (token.IsCancellationRequested) return null;

                Image image = LoadBitmapNoLock(bytes);
                lock (sync)
                {
                    if (cache.TryGetValue(url, out var raced))
                    {
                        image.Dispose();
                        return raced;
                    }
                    cache[url] = image;
                }
                return image;
            }
            catch
            {
                // Corrupt or locked cache file: fall through to a fresh download.
                return null;
            }
        }

        private static void SaveToDisk(string path, Image thumbnail)
        {
            try
            {
                byte[] png;
                using (var ms = new MemoryStream())
                {
                    thumbnail.Save(ms, ImageFormat.Png);
                    png = ms.ToArray();
                }

                // Write to a unique temp file then swap in, so a concurrent
                // reader never sees a half-written file.
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, png);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            catch
            {
                // The disk cache is best-effort; a failed write just means a
                // re-download next time.
            }
        }

        private static Image LoadBitmapNoLock(byte[] bytes)
        {
            // new Bitmap(img) copies the pixels, so the stream can be closed
            // immediately without the returned image losing its backing store.
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
                return new Bitmap(img);
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

        private static string DiskPath(string url, int width, int height)
        {
            var sb = new StringBuilder(url.Length + 12);
            foreach (char c in url)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            sb.Append('_').Append(width).Append('x').Append(height).Append(".png");
            return Path.Combine(cacheDir, sb.ToString());
        }
    }
}
