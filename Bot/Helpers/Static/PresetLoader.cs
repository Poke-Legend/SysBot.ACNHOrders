using NHSE.Core;
using System.IO;
using SysBot.Base;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public static class PresetLoader
    {
        public static Item[]? GetPreset(string nhiPath)
        {
            if (!File.Exists(nhiPath))
            {
                LogUtil.LogInfo($"{nhiPath} does not exist.", nameof(PresetLoader));
                return null;
            }

            var fileBytes = File.ReadAllBytes(nhiPath);
            if (!IsValidNhiFile(fileBytes))
            {
                LogUtil.LogInfo($"{nhiPath} is an invalid size for an NHI file.", nameof(PresetLoader));
                return null;
            }

            return Item.GetArray(fileBytes);
        }

        public static Item[]? GetPreset(OrderBotConfig cfg, string itemName, bool nhiOnly = true)
        {
            if (nhiOnly && !itemName.EndsWith(".nhi"))
                itemName += ".nhi";

            var path = Path.Combine(cfg.NHIPresetsDirectory, itemName);
            return GetPreset(path);
        }

        public static string[] GetPresets(OrderBotConfig cfg)
        {
            var filesInDirectory = Directory.GetFiles(cfg.NHIPresetsDirectory);
            return filesInDirectory.Select(Path.GetFileNameWithoutExtension)
                                   .Where(fileName => !string.IsNullOrEmpty(fileName))
                                   .ToArray()!;
        }

        public static string[] GetPresets(OrderBotConfig cfg, System.Collections.Generic.IEnumerable<string> additionalFiles)
        {
            var filesInDirectory = Directory.GetFiles(cfg.NHIPresetsDirectory);
            var allFiles = filesInDirectory.Concat(additionalFiles);
            return allFiles.Select(Path.GetFileNameWithoutExtension)
                           .Where(fileName => !string.IsNullOrEmpty(fileName))
                           .ToArray()!;
        }

        private static bool IsValidNhiFile(byte[] fileBytes)
        {
            return fileBytes.Length <= (Item.SIZE * 40) && fileBytes.Length != 0 && fileBytes.Length % 8 == 0;
        }
    }
}
