using BepInEx.Logging;
using RoR2;
using System;
using System.Linq;
using UnityEngine;

namespace CookBook
{
    internal static class ChatNetworkHandler
    {
        private const string NetPrefix = "CB_NET:";
        private static ManualLogSource _log;
        private static bool _enabled;

        internal static event Action<NetworkUser, string, int, int> OnIncomingObjective;
        internal static void Init(ManualLogSource log)
        {
            _log = log;
        }

        internal static void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            On.RoR2.Chat.AddMessage_string += OnAddMessage;
            _log.LogInfo("ChatNetworkHandler: Enabled (Listening for hidden objectives)");
        }

        internal static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;
            On.RoR2.Chat.AddMessage_string -= OnAddMessage;
            _log.LogInfo("ChatNetworkHandler: Disabled");
        }

        public static void SendObjectiveRequest(NetworkUser target, string command, int unifiedIdx, int quantity)
        {
            if (!_enabled || target == null) return;

            var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!localUser) return;

            string packet = $"{NetPrefix}{localUser.netId.Value}:{target.netId.Value}:{command}:{unifiedIdx}:{quantity}";

            _log.LogDebug($"[Net Out] Sending packet to {target.userName}: {packet}");

            RoR2.Console.instance.SubmitCmd(localUser, $"say \"{packet}\"");
        }

        private static void OnAddMessage(On.RoR2.Chat.orig_AddMessage_string orig, string message)
        {
            if (_enabled && message.Contains(NetPrefix))
            {
                int index = message.IndexOf(NetPrefix);
                ParseAndProcess(message.Substring(index + NetPrefix.Length));

                return;
            }
            orig(message);
        }

        private static void ParseAndProcess(string data)
        {
            try
            {
                string[] parts = data.Split(':');
                if (parts.Length < 5) return; // Increased to 5 parts

                uint senderID = uint.Parse(parts[0]);
                uint targetID = uint.Parse(parts[1]);
                string cmd = parts[2].ToUpper();
                int idx = int.Parse(parts[3]);
                int qty = int.Parse(parts[4]);

                var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (localUser != null && localUser.netId.Value == targetID)
                {
                    // Find the sender by their NetID to get their username
                    var senderUser = NetworkUser.readOnlyInstancesList
                        .FirstOrDefault(u => u.netId.Value == senderID);

                    OnIncomingObjective?.Invoke(senderUser, cmd, idx, qty);
                }
            }
            catch (Exception e) { _log.LogError($"[Net] Parse Error: {e.Message}"); }
        }
    }
}