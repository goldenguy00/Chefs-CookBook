using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;

namespace CookBook
{
    internal static class TradeTracker
    {
        private static readonly Dictionary<NetworkInstanceId, int> _usedTrades = new();
        private static int _maxTradesCache = 3; // Default fallback
        internal static event Action<IReadOnlyCollection<NetworkUser>, bool> OnTradeCountsChanged;


        internal static void Init()
        {
            SolusVendorShrineController.OnItemScrapped += OnItemScrapped;

            StateController.OnChefStageEntered += RefreshLimits;
        }

        private static void RefreshLimits()
        {
            _usedTrades.Clear();

            int oldMax = _maxTradesCache;

            var shrine = ComputationalExchangeController.instance?.solusVendorShrine?.GetComponent<SolusVendorShrineController>();
            if (shrine)
                _maxTradesCache = shrine.maxTrades;

            if (oldMax != _maxTradesCache)
                CookBook.Log.LogDebug($"[TradeTracker] maxTrades changed {oldMax} -> {_maxTradesCache}");
        }

        private static void OnItemScrapped(ItemTier tier, Interactor interactor) // fired when a user trades, we dont want stale paths so probably force a rebuild here
        {
            if (!interactor) return;

            var body = interactor.GetComponent<CharacterBody>();
            var networkUser = body?.master?.playerCharacterMasterController?.networkUser;
            if (!networkUser) return;

            var netId = networkUser.netId;
            if (!_usedTrades.TryGetValue(netId, out int used))
                used = 0;

            int before = Mathf.Max(0, _maxTradesCache - used);

            used++;
            _usedTrades[netId] = used;

            int after = Mathf.Max(0, _maxTradesCache - used);

            CookBook.Log.LogDebug($"[TradeTracker] {networkUser.userName} used a trade ({used}/{_maxTradesCache})");

            if (after != before)
            {
                OnTradeCountsChanged?.Invoke(new[] { networkUser }, true);
            }
        }

        public static int GetRemainingTrades(NetworkUser user)
        {
            if (!user) return 0;
            int used = _usedTrades.TryGetValue(user.netId, out int count) ? count : 0;
            return Mathf.Max(0, _maxTradesCache - used);
        }
    }
}