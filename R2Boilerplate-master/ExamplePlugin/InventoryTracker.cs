using BepInEx.Logging;
using RoR2;
using System;

namespace CookBook
{
    /// <summary>
    /// Tracks local player's items and raises an event when they change
    /// </summary>
    internal static class InventoryTracker
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _enabled;

        // Init local player's inventory
        private static Inventory _localInventory;
        private static int[] _stacks;

        /// <summary>
        /// Raised whenever an inventory's item counts change
        /// int[] is the current snapshot (do NOT mutate externally)
        /// </summary>
        internal static event Action<int[]> OnInventoryChanged;

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _log.LogInfo("InventoryTracker.Init()");

        }

        //--------------------------------------- State Control ----------------------------------------
        internal static void Enable()
        {
            if (_enabled)
                return;

            _enabled = true;

            CharacterBody.onBodyInventoryChangedGlobal += OnBodyInventoryChanged; // subscribe to inventory updates
            CharacterBody.onBodyStartGlobal += OnBodyStart; // subscribe for character initialization

            // clear stale user
            _localInventory = null;
            _stacks = null;

            _log.LogInfo("InventoryTracker.Enable()");


            if (TryBindFromExistingBodies())
            {
                CharacterBody.onBodyStartGlobal -= OnBodyStart;
                _log.LogInfo("InventoryTracker: late binding on Enable(), unsubscribed from onBodyStartGlobal");
            }
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            _enabled = false;
            _log.LogInfo("InventoryTracker.Disable()");

            CharacterBody.onBodyInventoryChangedGlobal -= OnBodyInventoryChanged; // remove subscription to inventory updates
            CharacterBody.onBodyStartGlobal -= OnBodyStart; // remove subscription to character initialization

            // clear stale user
            _localInventory = null;
            _stacks = null;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        /// <summary>
        /// bind the first time we see the local player's body
        /// </summary>
        private static void OnBodyStart(CharacterBody body)
        {
            if (!_enabled || body == null)
                return;

            var master = body.master;
            if (!master)
                return;

            var pcmc = master.playerCharacterMasterController;
            if (!pcmc)
                return;

            var networkUser = pcmc.networkUser;
            if (!networkUser)
                return;

            var localUser = GetLocalUser();
            if (networkUser.localUser != localUser)
                return;

            // Only care about the local player's body.
            if (networkUser.localUser != localUser)
                return;

            var inv = body.inventory;
            if (inv == null)
                return;

            _log.LogInfo("InventoryTracker: bound to local player inventory (OnBodyStart event)");
            _log.LogInfo("InventoryTracker: unsubscribed from onBodyStartGlobal (binding complete)");
            SnapshotFromInventory(inv);

            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            return;
        }

        /// <summary>
        /// Called by RoR2 whenever any body's inventory changes 
        /// </summary>
        private static void OnBodyInventoryChanged(CharacterBody body)
        {
            if (!_enabled || body == null || body.inventory == null)
                return;

            if (_localInventory != null)
            {
                if (!ReferenceEquals(body.inventory, _localInventory))
                {
                    return; // Ignore non-local inventory changes
                }

                _log.LogDebug("[InventoryTracker] Local inventory changed, refreshing snapshot");
                SnapshotFromInventory(_localInventory);
                return;
            }
        }

        //----------------------------------- Fallback Binding -----------------------------------
        private static bool TryBindFromExistingBodies()
        {
            var localUser = GetLocalUser();
            if (localUser == null)
            {
                _log.LogDebug("[InventoryTracker] TryBindFromExistingBodies: no LocalUsers present");
                return false;
            }

            // iterate over all existing bodies to find local player
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (!body || body.inventory == null)
                    continue;

                var master = body.master;
                if (!master)
                    continue;

                var pcmc = master.playerCharacterMasterController;
                if (!pcmc)
                    continue;

                var networkUser = pcmc.networkUser;
                if (!networkUser)
                    continue;

                // Is this body controlled by our local user?
                if (networkUser.localUser != localUser)
                    continue;

                _log.LogInfo($"InventoryTracker: bound via TryBindFromExistingBodies() on body {body.name}");
                SnapshotFromInventory(body.inventory);
                return true;
            }

            _log.LogDebug("[InventoryTracker] TryBindFromExistingBodies: no matching local body found.");
            return false;
        }


        //--------------------------------------- Helpers ----------------------------------------

        private static void SnapshotFromInventory(Inventory inv)
        {
            _localInventory = inv;

            int len = ItemCatalog.itemCount;

            // Build/refresh the snapshot
            if (_stacks == null || _stacks.Length != len)
            {
                _stacks = new int[len];
            }
            for (int i = 0; i < len; i++)
            {
                _stacks[i] = inv.GetItemCount((ItemIndex)i);
            }

            // [DEBUG]: print the snapshot
            _log.LogInfo("InventoryTracker: snapshot after change/bind:");

            for (int i = 0; i < len; i++)
            {
                int count = _stacks[i];
                if (count <= 0)
                    continue;

                ItemIndex idx = (ItemIndex)i;
                ItemDef def = ItemCatalog.GetItemDef(idx);
                string name = def ? def.nameToken : idx.ToString();

                _log.LogInfo($"  [Tracker] {name} x{count}");
            }
            OnInventoryChanged?.Invoke(_stacks);
        }

        /// <summary>
        /// returns a copy of inventory stacks
        /// </summary>
        internal static int[] GetStacksCopy()
        {
            if (_stacks == null)
                return null;

            var copy = new int[_stacks.Length];
            Array.Copy(_stacks, copy, _stacks.Length);
            return copy;
        }

        /// <summary>
        /// get the first local user
        /// </summary>
        private static LocalUser GetLocalUser()
        {
            var list = LocalUserManager.readOnlyLocalUsersList;
            return list.Count > 0 ? list[0] : null;
        }
    }
}
