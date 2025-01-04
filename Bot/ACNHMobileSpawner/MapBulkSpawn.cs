using NHSE.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using static NHSE.Core.ItemKind;

namespace ACNHMobileSpawner
{
    /// <summary>
    /// Manages bulk spawning of items on the map with various presets
    /// (music, DIY recipes, fossils, etc.).
    /// </summary>
    public class MapBulkSpawn
    {
        public enum BulkSpawnPreset
        {
            Music,
            DIYRecipesAlphabetical,
            DIYRecipesSequential,
            Fossils,
            GenericMaterials,
            SeasonalMaterials,
            RealArt,
            FakeArt,
            Bugs,
            Fish,
            BugsAndFish,
            InventoryOfApp, // Not used in snippet, but can be expanded if needed
            CustomFile
        }

        public enum SpawnDirection
        {
            SouthEast,
            SouthWest,
            NorthWest,
            NorthEast
        }

        public BulkSpawnPreset CurrentSpawnPreset { get; private set; } = 0;
        public SpawnDirection CurrentSpawnDirection { get; private set; } = 0;

        /// <summary>
        /// How many copies of each item to spawn.
        /// </summary>
        public int Multiplier => 1;

        /// <summary>
        /// The rectangle width dimension used in some spawn calculations (not shown here).
        /// </summary>
        public float RectWidthDimension => 1;

        /// <summary>
        /// The rectangle height dimension used in some spawn calculations (not shown here).
        /// </summary>
        public float RectHeightDimension => 1;

        /// <summary>
        /// If true, existing tiles can be overwritten by new items.
        /// </summary>
        public bool OverwriteTiles => true;

        private static IReadOnlyList<ushort>? allItems = null;

        /// <summary>
        /// Caches and returns a read-only list of all valid item IDs from the game data source.
        /// </summary>
        public static IReadOnlyList<ushort> GetAllItems()
        {
            if (allItems == null)
            {
                var listItems = GameInfo.Strings.ItemDataSource.ToList();
                var itemsClean = listItems.Where(x => !x.Text.StartsWith("(Item #")).ToList();

                var items = new ushort[itemsClean.Count];
                for (int i = 0; i < itemsClean.Count; ++i)
                {
                    items[i] = (ushort)itemsClean[i].Value;
                }
                allItems = items;
            }
            return allItems;
        }

        /// <summary>
        /// Loaded items from a custom file (if using the CustomFile preset).
        /// </summary>
        private Item[] fileLoadedItems = { new Item(0x09C4) }; // By default, 09C4 = tree branch.

        public MapBulkSpawn()
        {
            // Default constructor
        }

        /// <summary>
        /// Returns how many items are in the current preset.
        /// </summary>
        private int getItemCount()
        {
            return GetItemsOfCurrentPreset().Length;
        }

        /// <summary>
        /// Ensures all file-loaded items have system param 0x20. 
        /// (Might be for placing them properly in the map.)
        /// </summary>
        private void Flag20LoadedItems()
        {
            foreach (Item i in fileLoadedItems)
                i.SystemParam = 0x20;
        }

        /// <summary>
        /// Gets the items from the current preset.
        /// </summary>
        public Item[] GetItemsOfCurrentPreset()
        {
            return GetItemsOfPreset(CurrentSpawnPreset);
        }

        /// <summary>
        /// Builds a list of items for a given preset, optionally with a custom system param (flag0).
        /// </summary>
        public Item[] GetItemsOfPreset(BulkSpawnPreset preset, byte flag0 = 0x20)
        {
            var toRet = new List<Item>();

            switch (preset)
            {
                case BulkSpawnPreset.Music:
                    toRet.AddRange(GetItemsOfKind(Kind_Music));
                    break;
                case BulkSpawnPreset.DIYRecipesAlphabetical:
                    toRet.AddRange(GetDIYRecipes(alphabetical: true));
                    break;
                case BulkSpawnPreset.DIYRecipesSequential:
                    toRet.AddRange(GetDIYRecipes(alphabetical: false));
                    break;
                case BulkSpawnPreset.Fossils:
                    toRet.AddRange(GetItemsOfKind(Kind_Fossil));
                    break;
                case BulkSpawnPreset.GenericMaterials:
                    toRet.AddRange(GetItemsOfKind(Kind_Ore, Kind_CraftMaterial));
                    break;
                case BulkSpawnPreset.SeasonalMaterials:
                    toRet.AddRange(GetItemsOfKind(
                        Kind_Vegetable, Kind_Sakurapetal, Kind_ShellDrift, Kind_TreeSeedling,
                        Kind_CraftMaterial, Kind_Mushroom, Kind_AutumnLeaf, Kind_SnowCrystal
                    ));
                    break;
                case BulkSpawnPreset.RealArt:
                    toRet.AddRange(GetItemsOfKind(Kind_Picture, Kind_Sculpture));
                    break;
                case BulkSpawnPreset.FakeArt:
                    toRet.AddRange(GetItemsOfKind(Kind_PictureFake, Kind_SculptureFake));
                    break;
                case BulkSpawnPreset.Bugs:
                    toRet.AddRange(GetItemsOfKind(Kind_Insect));
                    break;
                case BulkSpawnPreset.Fish:
                    toRet.AddRange(GetItemsOfKind(Kind_Fish, Kind_ShellFish, Kind_DiveFish));
                    break;
                case BulkSpawnPreset.BugsAndFish:
                    toRet.AddRange(GetItemsOfKind(Kind_Fish, Kind_ShellFish, Kind_DiveFish));
                    toRet.AddRange(GetItemsOfKind(Kind_Insect));
                    break;
                case BulkSpawnPreset.CustomFile:
                    toRet.AddRange(fileLoadedItems);
                    break;
                default:
                    // If not recognized, spawn a single tree branch
                    toRet.Add(new Item(0x09C4));
                    break;
            }

            // For non-CustomFile presets, set system param & try to stack to max
            if (preset != BulkSpawnPreset.CustomFile)
            {
                foreach (Item i in toRet)
                {
                    i.SystemParam = flag0;

                    // If not a recipe, message bottle, or fossil, see if we can stack
                    var kind = ItemInfo.GetItemKind(i);
                    if (kind != Kind_DIYRecipe && kind != Kind_MessageBottle && kind != Kind_Fossil)
                        if (ItemInfo.TryGetMaxStackCount(i, out ushort max))
                            i.Count = (ushort)(max - 1);
                }
            }

            // If we have a multiplier other than 1, replicate items that many times
            int mul = Multiplier;
            if (mul != 1)
            {
                var multipliedItemList = new List<Item>();
                foreach (var item in toRet)
                {
                    for (int i = 0; i < mul; ++i)
                        multipliedItemList.Add(item);
                }
                toRet = multipliedItemList;
            }

            return toRet.ToArray();
        }

        /// <summary>
        /// Returns all items whose kind is in the specified array of ItemKind values.
        /// </summary>
        private Item[] GetItemsOfKind(params ItemKind[] kinds)
        {
            var toRet = new List<ushort>();
            foreach (var kind in kinds)
            {
                var filtered = GetAllItems().Where(x => ItemInfo.GetItemKind(x) == kind);
                toRet.AddRange(filtered);
            }

            var asItems = new Item[toRet.Count];
            for (int i = 0; i < toRet.Count; ++i)
                asItems[i] = new Item(toRet[i]);

            return asItems;
        }

        /// <summary>
        /// Builds an array of items representing DIY recipes (Item.DIYRecipe),
        /// either in alphabetical or sequential order, using RecipeList.Recipes.
        /// </summary>
        private Item[] GetDIYRecipes(bool alphabetical = true)
        {
            var recipes = RecipeList.Recipes;
            var retRecipes = new List<Item>();

            foreach (var recipe in recipes)
            {
                // recipe.Key is typically an index for the recipe, store it in Count
                var itemRecipe = new Item(Item.DIYRecipe) { Count = recipe.Key };
                retRecipes.Add(itemRecipe);
            }

            if (alphabetical)
            {
                // Sort by the displayed name
                var ordered = retRecipes.OrderBy(x => getRecipeName(x.Count, recipes));
                retRecipes = ordered.ToList();
            }

            return retRecipes.ToArray();
        }

        /// <summary>
        /// Looks up the in-game display name for the recipe, used for sorting.
        /// </summary>
        private string getRecipeName(ushort count, IReadOnlyDictionary<ushort, ushort> recipes)
        {
            var currentRecipeItem = recipes[count];
            return GameInfo.Strings.itemlistdisplay[currentRecipeItem].ToLower();
        }

        /// <summary>
        /// Trims trailing "no item" entries from the buffer up to the first item that is not 'trimValue'.
        /// For instance, if 'trimValue' is 0 (null item).
        /// </summary>
        public static void TrimTrailingNoItems(ref Item[] buffer, ushort trimValue)
        {
            int i = buffer.Length;
            while (i > 0 && buffer[--i].ItemId == trimValue)
            {
                // no-op inside loop
            }
            Array.Resize(ref buffer, i + 1);
        }
    }
}
