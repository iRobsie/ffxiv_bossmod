using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ImGuiNET;
using System;
using System.Linq;

namespace BossMod.AI
{
    class AIManager : IDisposable
    {
        private Autorotation _autorot;
        private AIController _controller;
        private AIConfig _config;
        private int _masterSlot = PartyState.PlayerSlot; // non-zero means corresponding player is master
        private AIBehaviour? _beh;
        private UISimpleWindow _ui;

        public AIManager(Autorotation autorot)
        {
            _autorot = autorot;
            _controller = new();
            _config = Service.Config.Get<AIConfig>();
            _ui = new("AI", DrawOverlay, false, new(100, 100), ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing) { RespectCloseHotkey = false };
            Service.ChatGui.ChatMessage += OnChatMessage;
        }

        public void Dispose()
        {
            SwitchToIdle();
            _ui.Dispose();
            Service.ChatGui.ChatMessage -= OnChatMessage;
        }

        public void Update()
        {
            if (_autorot.WorldState.Party.ContentIDs[_masterSlot] == 0)
                SwitchToIdle();

            if (!_config.Enabled && _beh != null)
                SwitchToIdle();

            var player = _autorot.WorldState.Party.Player();
            var master = _autorot.WorldState.Party[_masterSlot];
            if (_beh != null && player != null && master != null)
            {
                _beh.Execute(player, master);
            }
            else
            {
                _controller.Clear();
            }
            _controller.Update(player);

            _ui.IsOpen = _config.Enabled && player != null;
        }

        private void DrawOverlay()
        {
            ImGui.TextUnformatted($"AI: {(_beh != null ? "on" : "off")}, master={_autorot.WorldState.Party[_masterSlot]?.Name}");
            ImGui.TextUnformatted($"Navi={_controller.NaviTargetPos} / {_controller.NaviTargetRot}{(_controller.ForceFacing ? " forced" : "")}");
            ImGui.Text($"Follow Self: {(_config.SelfMaster ? "Enabled" : "Disabled")}"); // Display the Follow Self state
            _beh?.DrawDebug();
            if (ImGui.Button("Reset"))
                SwitchToIdle();
            ImGui.SameLine();
            if (ImGui.Button("Follow leader"))
            {
                var leader = Service.PartyList[(int)Service.PartyList.PartyLeaderIndex];
                int leaderSlot = leader != null ? _autorot.WorldState.Party.ContentIDs.IndexOf((ulong)leader.ContentId) : -1;
                SwitchToFollow(leaderSlot >= 0 ? leaderSlot : PartyState.PlayerSlot);
            }
            ImGui.SameLine();
            if (ImGui.Button(_config.SelfMaster ? "Disable Follow Self" : "Enable Follow Self"))
            {
                _config.SelfMaster = !_config.SelfMaster; // Toggle the state of SelfMaster
                if (_config.SelfMaster && _masterSlot == -1)
                {
                    SwitchToFollow(PartyState.PlayerSlot);
                }
                // Add any additional code you need to handle "Follow Self" here
            }
        }



        private void SwitchToIdle()
        {
            _beh?.Dispose();
            _beh = null;

            _masterSlot = PartyState.PlayerSlot;
            _controller.Clear();
        }

        private void SwitchToFollow(int masterSlot)
        {
            SwitchToIdle();

            // Check if "Follow Self" is enabled
            if (_config.SelfMaster == true)
            {
                // Set the masterSlot to the player's slot
                _masterSlot = PartyState.PlayerSlot;
            }
            else
            {
                // Set the masterSlot to follow the provided masterSlot (leader or another party member)
                _masterSlot = masterSlot;
            }

            // Create the AIBehaviour with the updated _config.SelfMaster value
            _beh = new AIBehaviour(_controller, _autorot, _config.SelfMaster);
        }


        private int FindPartyMemberSlotFromSender(SeString sender)
        {
            var source = sender.Payloads.FirstOrDefault() as PlayerPayload;
            if (source == null)
                return -1;
            var pm = Service.PartyList.FirstOrDefault(pm => pm.Name.TextValue == source.PlayerName && pm.World.Id == source.World.RowId);
            if (pm == null)
                return -1;
            return _autorot.WorldState.Party.ContentIDs.IndexOf((ulong)pm.ContentId);
        }

        private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (!_config.Enabled || type != XivChatType.Party)
                return;

            var messagePrefix = message.Payloads.FirstOrDefault() as TextPayload;
            if (messagePrefix?.Text == null || !messagePrefix.Text.StartsWith("vbmai "))
                return;

            var messageData = messagePrefix.Text.Split(' ');
            if (messageData.Length < 2)
                return;

            switch (messageData[1])
            {
                case "follow":
                    var master = FindPartyMemberSlotFromSender(sender);
                    if (master >= 0)
                        SwitchToFollow(master);
                    break;
                case "cancel":
                    SwitchToIdle();
                    break;
                default:
                    Service.Log($"[AI] Unknown command: {messageData[1]}");
                    break;
            }
        }
    }
}
