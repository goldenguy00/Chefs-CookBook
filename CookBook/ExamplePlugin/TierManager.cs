using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CookBook
{
    internal static class TierManager
    {
        // ---------------------- Fields  ----------------------------
        private static bool _initialized;
        private static ManualLogSource _log;
        private static readonly ItemTier[] DefaultOrder; 
        private static readonly HashSet<ItemTier> _seenTiers ;
        private static Dictionary<ItemTier, int> _orderMap;

    // Events
    internal static event System.Action OnTierOrderChanged;

        // ---------------------- Initialization  ----------------------------
        static TierManager()
        {
            DefaultOrder = new[]
            {
                ItemTier.Tier3,
                ItemTier.Boss,
                ItemTier.Tier2,
                ItemTier.Tier1,
                ItemTier.VoidTier3,
                ItemTier.VoidTier2,
                ItemTier.VoidTier1,
                ItemTier.Lunar,
                ItemTier.AssignedAtRuntime,
                ItemTier.NoTier
            };
            _seenTiers = new HashSet<ItemTier>(DefaultOrder);
            _orderMap = BuildMapFrom(DefaultOrder);
        }

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;

            log.LogInfo("TierManager.Init()");
        }

        // ---------------------- Tier Events ----------------------------
        internal static void OnTierOrderConfigChanged(object _, EventArgs __)
        {
            TierManager.SetOrder(TierManager.ParseTierOrder(CookBook.TierOrder.Value));
        }

        // ---------------------- Runtime Helpers ----------------------------
        /// <summary>
        /// Collects all tiers used by any item in the ItemCatalog.
        /// </summary>
        internal static ItemTier[] DiscoverTiersFromCatalog() 
        {
            var set = new HashSet<ItemTier>(_seenTiers);
            int len = ItemCatalog.itemCount;
            for (int i = 0; i < len; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (!def)
                    continue;

                set.Add(def.tier);
            }

            // Record discovered tiers.
            foreach (var t in set)
            {
                _seenTiers.Add(t);
            }

            return set.ToArray();
        }

        /// <summary>
        /// Convert an ItemTier[] ordering into the CSV format used by the config.
        /// </summary>
        internal static string ToCsv(ItemTier[] order)
        {
            return string.Join(",", order.Select(t => t.ToString()));
        }

        /// <summary>
        /// Merge the current config order with a discovered set of tiers.
        /// Preserves config order.
        /// Appends newly discovered tiers to the end.
        /// </summary>
        internal static ItemTier[] MergeOrder(ItemTier[] fromConfig, ItemTier[] discovered)
        {
            var list = new List<ItemTier>(fromConfig.Length + discovered.Length);
            var seen = new HashSet<ItemTier>();

            foreach (var t in fromConfig)
            {
                if (seen.Add(t))
                    list.Add(t);
            }

            foreach (var t in discovered)
            {
                if (seen.Add(t))
                    list.Add(t);
            }

            // fallback
            return list.Count > 0 ? list.ToArray() : DefaultOrder;
        }

        // ---------------------- Sorting Helpers ----------------------------
        /// <summary>
        /// Compare two CraftableEntry objects.
        /// Items sorted by tier/name; Equipment sorted by name.
        /// </summary>
        internal static int CompareCraftableEntries(
            CraftPlanner.CraftableEntry a,
            CraftPlanner.CraftableEntry b)
        {
            // Group by result kind | items, equipment
            int kindCmp = a.ResultKind.CompareTo(b.ResultKind);
            if (kindCmp != 0)
            {
                return kindCmp;
            }

            // Sort Items
            if (a.ResultKind == RecipeResultKind.Item)
            {
                return CompareItems(a.ResultItem, b.ResultItem);
            }
                
            // Order Equipment Alphabetically
            var defA = EquipmentCatalog.GetEquipmentDef(a.ResultEquipment);
            var defB = EquipmentCatalog.GetEquipmentDef(b.ResultEquipment);

            string nameA = defA ? defA.nameToken : a.ResultEquipment.ToString();
            string nameB = defB ? defB.nameToken : b.ResultEquipment.ToString();
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compare two ItemIndex values (tier first, then name).
        /// </summary>
        internal static int CompareItems(ItemIndex a, ItemIndex b)
        {
            var defA = ItemCatalog.GetItemDef(a);
            var defB = ItemCatalog.GetItemDef(b);

            var tierA = defA ? defA.tier : ItemTier.NoTier;
            var tierB = defB ? defB.tier : ItemTier.NoTier;

            int rankA = Rank(tierA);
            int rankB = Rank(tierB);
            int tierCmp = rankA.CompareTo(rankB);
            if (tierCmp != 0)
                return tierCmp;

            string nameA = defA ? defA.nameToken : a.ToString();
            string nameB = defB ? defB.nameToken : b.ToString();
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the integer rank for a given tier.
        /// </summary>
        /// 
        internal static int Rank(ItemTier tier)
        {
            _seenTiers.Add(tier);

            // If explicitly mapped, use it
            if (_orderMap.TryGetValue(tier, out int rank))
            {
                return rank;
            }

            // Default: unknown/modded tiers after natives
            return _orderMap.Count + (int)tier;
        }

        // ---------------------- Helpers ----------------------------
        private static Dictionary<ItemTier, int> BuildMapFrom(ItemTier[] arr)
        {
            var map = new Dictionary<ItemTier, int>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                map[arr[i]] = i;
            return map;
        }

        /// <summary>
        /// Update the tier order
        /// </summary>
        internal static void SetOrder(ItemTier[] newOrder)
        {
            _orderMap = BuildMapFrom(newOrder);
            OnTierOrderChanged?.Invoke();
        }

        internal static ItemTier[] GetAllKnownTiers()
        {
            return _seenTiers.ToArray();
        }
        internal static ItemTier[] ParseTierOrder(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return DefaultOrder;

            var parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<ItemTier>();
            foreach (var p in parts)
            {
                if (Enum.TryParse<ItemTier>(p.Trim(), out var tier))
                    list.Add(tier);
            }

            // Fall back if user nukes config
            return list.Count > 0 ? list.ToArray() : DefaultOrder;
        }
    }
}
