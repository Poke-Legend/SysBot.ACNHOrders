using Discord;
using NHSE.Core;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class NetUtil
    {
        private static readonly HttpClient httpClient = new();

        /// <summary>
        /// Asynchronously downloads the byte[] from the specified URL.
        /// </summary>
        public static async Task<byte[]> DownloadFromUrlAsync(string url)
        {
            HttpResponseMessage response = await httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronously downloads the byte[] from the specified URL.
        /// </summary>
        public static byte[] DownloadFromUrlSync(string url)
        {
            HttpResponseMessage response = httpClient.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Downloads an NHI attachment (IAttachment from Discord) and parses it into Item[].
        /// Checks basic size constraints using MultiItem.MaxOrder * Item.SIZE.
        /// </summary>
        public static async Task<Download<Item[]>> DownloadNHIAsync(IAttachment att)
        {
            var result = new Download<Item[]>
            {
                SanitizedFileName = Format.Sanitize(att.Filename) // If you have a Format class
            };

            // Validate size for the Items array
            // e.g., if MultiItem.MaxOrder = 40, Item.SIZE might be 0x8 or 0x10
            if ((att.Size > MultiItem.MaxOrder * Item.SIZE) || att.Size < Item.SIZE)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid size.";
                return result;
            }

            // Download the resource and load the bytes into a buffer
            string url = att.Url;
            var buffer = await DownloadFromUrlAsync(url).ConfigureAwait(false);

            // Convert bytes to an Item[] using NHSE.Core
            var items = Item.GetArray(buffer);
            if (items == null)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Invalid NHI attachment.";
                return result;
            }

            result.Data = items;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Asynchronously downloads a file from a URL to a destination path.
        /// </summary>
        public static async Task DownloadFileAsync(string url, string destinationFilePath)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync();
            await using FileStream fileStream = new(
                destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true
            );
            await contentStream.CopyToAsync(fileStream);
        }
    }

    /// <summary>
    /// Generic download result carrying success status, the data (if any), plus error or filename info.
    /// </summary>
    public sealed class Download<T> where T : class
    {
        public bool Success;
        public T? Data;
        public string? SanitizedFileName;
        public string? ErrorMessage;
    }
}
