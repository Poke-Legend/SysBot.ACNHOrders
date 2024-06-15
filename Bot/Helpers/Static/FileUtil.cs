using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace SysBot.ACNHOrders
{
    public static class FileUtil
    {
        private const int MaxRetryAttempts = 5;
        private const int DelayBetweenRetries = 1000; // 1 second

        public static async Task WriteBytesToFileAsync(byte[] bytes, string path, CancellationToken token)
        {
            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    using (FileStream sourceStream = new FileStream(path,
                        FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: 4096, useAsync: true))
                    {
                        await sourceStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                    }
                    return; // Success, exit method
                }
                catch (IOException ex) when (attempt < MaxRetryAttempts - 1)
                {
                    Console.WriteLine($"Attempt {attempt + 1} failed: {ex.Message}. Retrying in {DelayBetweenRetries}ms...");
                    await Task.Delay(DelayBetweenRetries, token).ConfigureAwait(false);
                }
            }

            // If the code reaches here, all attempts have failed
            throw new IOException($"Failed to write to file '{path}' after {MaxRetryAttempts} attempts.");
        }

        public static string GetEmbeddedResource(string namespacename, string filename)
        {
            var assembly = Assembly.GetExecutingAssembly();
            if (assembly == null)
                return string.Empty;
            var resourceName = namespacename + "." + filename;
#pragma warning disable CS8600, CS8604
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();
                return result;
            }
#pragma warning restore CS8600, CS8604
        }
    }
}
