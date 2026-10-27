using System.Collections.Generic;
using System.Text;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Social;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Gruppe und Chat eines Spielers. Der Server vermittelt alles:
    ///   Chat   – Umgebung (Rufweite), Gruppe, Fluestern; Text wird entschaerft und begrenzt, Spamschutz.
    ///   Gruppe – Einladen per Name, Annehmen/Ablehnen, Verlassen, Entfernen (Leitung); Mitgliederstand 4× pro Sekunde.
    /// Gruppenmitglieder in der Naehe teilen Beuteberechtigung, Erfahrung und Toetungsziele (PartyRegistry).
    /// </summary>
    [RequireComponent(typeof(NetworkInventory))]
    [DisallowMultipleComponent]
    public sealed class NetworkSocial : NetworkBehaviour, ISocialService
    {
        public const float SayRange = 40f;
        private const int MaxChatLines = 100;
        private const int SpamMessages = 5;
        private const float SpamWindow = 5f;

        private static readonly Dictionary<string, NetworkSocial> ByProfile = new Dictionary<string, NetworkSocial>();
        /// <summary>Zuletzt bekannte Anzeige je Profil (fuer Mitglieder, die gerade offline sind).</summary>
        private static readonly Dictionary<string, (string name, int level, string cls)> Known = new Dictionary<string, (string, int, string)>();

        private NetworkInventory _inv;
        private NetworkPlayer _player;
        private Health _health;
        private PlayerIdentity _identity;

        // ---- Server
        private string _profile;
        private readonly Queue<float> _recent = new Queue<float>();
        private float _partyTimer;
        private string _lastParty;

        // ---- Client (Besitzer)
        private readonly List<PartyMemberView> _cParty = new List<PartyMemberView>();
        private readonly List<ChatLine> _cChat = new List<ChatLine>();
        private string _cInvite;
        private int _cRev;

        public int ClientRevision => _cRev;

        private void Awake()
        {
            _inv = GetComponent<NetworkInventory>();
            _player = GetComponent<NetworkPlayer>();
            _health = GetComponent<Health>();
            _identity = GetComponent<PlayerIdentity>();
        }

        // ================================================================== Start / Ende

        public override void OnStartServer()
        {
            base.OnStartServer();
            _inv.ServerLoggedIn += OnServerLogin;
            // Gruppen in die Beuteberechtigung einhaengen: nur Mitglieder in der Naehe teilen
            PartyRegistry.MembersOf = PartyService.MembersOf;
            PartyRegistry.IsNear = (profile, pos) =>
                NetworkInventory.Online.TryGetValue(profile, out NetworkInventory i) && i != null &&
                Vector3.Distance(i.transform.position, pos) <= PartyService.ShareRange;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _inv.ServerLoggedIn -= OnServerLogin;
            if (_profile != null && ByProfile.TryGetValue(_profile, out NetworkSocial cur) && cur == this)
            {
                ByProfile.Remove(_profile);
                PartyService.Party p = PartyService.Of(_profile);
                if (p != null) SystemToParty(p, $"{Known[_profile].name} ist offline.", null);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) SocialClient.Register(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) SocialClient.Unregister(this);
        }

        private void OnDestroy() => SocialClient.Unregister(this);

        private void OnServerLogin()
        {
            _profile = _inv.ProfileId;
            ByProfile[_profile] = this;
            Remember();
            _lastParty = null;
            PartyService.Party p = PartyService.Of(_profile);
            if (p != null) SystemToParty(p, $"{MyServerName} ist wieder da.", null);
            string inviter = PartyService.PendingInviteFrom(_profile);
            if (inviter != null && Known.TryGetValue(inviter, out var k)) TargetInvite(Owner, k.name);
        }

        private string MyServerName => _inv != null ? _inv.CharacterName : "";

        private void Remember()
        {
            if (_profile == null || _inv.ServerModel == null) return;
            Known[_profile] = (MyServerName, _inv.ServerModel.Level, _inv.ServerModel.Class != null ? _inv.ServerModel.Class.DisplayName : "");
        }

        private static NetworkSocial FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (NetworkSocial s in ByProfile.Values)
                if (s != null && string.Equals(s.MyServerName, name.Trim(), System.StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        // ================================================================== Server: Chat

        [ServerRpc]
        private void ServerChat(byte channel, string target, string text)
        {
            if (_profile == null) return;
            text = SocialClient.Clean(text);
            if (text.Length == 0) return;
            float now = Time.unscaledTime;
            while (_recent.Count > 0 && now - _recent.Peek() > SpamWindow) _recent.Dequeue();
            if (_recent.Count >= SpamMessages) { SystemTo(this, "Nicht so schnell."); return; }
            _recent.Enqueue(now);

            switch ((ChatChannel)channel)
            {
                case ChatChannel.Say:
                    foreach (NetworkSocial s in ByProfile.Values)
                        if (s != null && Vector3.Distance(s.transform.position, transform.position) <= SayRange) s.Deliver(ChatChannel.Say, MyServerName, text);
                    break;
                case ChatChannel.Party:
                {
                    PartyService.Party p = PartyService.Of(_profile);
                    if (p == null) { SystemTo(this, "Du bist in keiner Gruppe."); return; }
                    foreach (string m in p.Members)
                        if (ByProfile.TryGetValue(m, out NetworkSocial s) && s != null) s.Deliver(ChatChannel.Party, MyServerName, text);
                    break;
                }
                case ChatChannel.Whisper:
                {
                    NetworkSocial to = FindByName(target);
                    if (to == null) { SystemTo(this, $"Spieler \"{SocialClient.Clean(target, 16)}\" ist nicht online."); return; }
                    if (to == this) { SystemTo(this, "Du fluesterst mit dir selbst. Niemand antwortet."); return; }
                    to.Deliver(ChatChannel.Whisper, MyServerName, text);
                    Deliver(ChatChannel.WhisperSent, to.MyServerName, text);
                    break;
                }
            }
        }

        private void Deliver(ChatChannel ch, string from, string text)
        {
            if (Owner != null && Owner.IsActive) TargetChat(Owner, (byte)ch, from ?? "", text);
        }

        private static void SystemTo(NetworkSocial s, string text) => s?.Deliver(ChatChannel.System, "", text);

        /// <summary>Server: Systemnachricht an einen Spieler (z. B. "Du betrittst die Salzgruben").</summary>
        public static void ServerSystemMessage(string profile, string text)
        {
            if (profile != null && ByProfile.TryGetValue(profile, out NetworkSocial s)) SystemTo(s, text);
        }

        /// <summary>Server: Signal einer Boss-Begegnung an einen Spieler (Flut, Tor …) – siehe <see cref="Ashveil.Core.EncounterCues"/>.</summary>
        public static void ServerCue(string profile, Vector3 where, string cue, float value)
        {
            if (profile != null && ByProfile.TryGetValue(profile, out NetworkSocial s) && s != null && s.Owner != null && s.Owner.IsValid)
                s.TargetCue(s.Owner, where, cue, value);
        }

        [TargetRpc]
        private void TargetCue(NetworkConnection conn, Vector3 where, string cue, float value)
        {
            // Der Host hat das Signal als Server schon selbst ausgeloest (gleiche Szenenobjekte)
            if (IsServerInitialized) return;
            Ashveil.Core.EncounterCues.Raise(where, cue, value);
        }

        private static void SystemToParty(PartyService.Party p, string text, string except)
        {
            foreach (string m in p.Members)
                if (m != except && ByProfile.TryGetValue(m, out NetworkSocial s)) SystemTo(s, text);
        }

        // ================================================================== Server: Gruppe

        [ServerRpc]
        private void ServerInvite(string name)
        {
            if (_profile == null) return;
            NetworkSocial to = FindByName(name);
            if (to == null) { SystemTo(this, $"Spieler \"{SocialClient.Clean(name, 16)}\" ist nicht online."); return; }
            string err = PartyService.Invite(_profile, to._profile);
            if (err != null) { SystemTo(this, err); return; }
            Remember();
            to.TargetInvite(to.Owner, MyServerName);
            SystemTo(this, $"Einladung an {to.MyServerName} geschickt.");
        }

        [ServerRpc]
        private void ServerRespond(bool accept)
        {
            if (_profile == null) return;
            string from = PartyService.PendingInviteFrom(_profile);
            if (!accept)
            {
                PartyService.Decline(_profile);
                TargetInvite(Owner, "");
                if (from != null && ByProfile.TryGetValue(from, out NetworkSocial inviter)) SystemTo(inviter, $"{MyServerName} lehnt die Einladung ab.");
                return;
            }
            PartyService.Party p = PartyService.Accept(_profile, out string err);
            TargetInvite(Owner, "");
            if (p == null) { SystemTo(this, err); return; }
            Remember();
            SystemToParty(p, $"{MyServerName} ist der Gruppe beigetreten.", null);
            PushPartyNow(p);
        }

        [ServerRpc]
        private void ServerLeave()
        {
            if (_profile == null) return;
            PartyService.Party before = PartyService.Of(_profile);
            var formerMembers = before != null ? new List<string>(before.Members) : new List<string>();
            PartyService.Party p = PartyService.Leave(_profile, IsOnline);   // 21.10. (MEHRSPIELER-04): Leitung an ein Online-Mitglied
            SystemTo(this, "Du hast die Gruppe verlassen.");
            SendParty(this, null);
            if (p != null) { SystemToParty(p, $"{MyServerName} hat die Gruppe verlassen.", null); PushPartyNow(p); }
            else
            {
                foreach (string m in formerMembers)
                    if (m != _profile && ByProfile.TryGetValue(m, out NetworkSocial s)) SystemTo(s, $"{MyServerName} hat die Gruppe verlassen – die Gruppe ist aufgeloest.");
                NotifyDissolved();
            }
        }

        [ServerRpc]
        private void ServerKick(string name)
        {
            if (_profile == null) return;
            PartyService.Party p = PartyService.Of(_profile);
            if (p == null || p.Leader != _profile) { SystemTo(this, "Nur die Gruppenleitung kann Mitglieder entfernen."); return; }
            string victim = null;
            foreach (string m in p.Members)
                if (Known.TryGetValue(m, out var k) && string.Equals(k.name, name, System.StringComparison.OrdinalIgnoreCase)) victim = m;
            if (victim == null || victim == _profile) { SystemTo(this, "Nicht in deiner Gruppe."); return; }
            string vName = Known[victim].name;
            PartyService.Party rest = PartyService.Leave(victim, IsOnline);   // 21.10. (MEHRSPIELER-04)
            if (ByProfile.TryGetValue(victim, out NetworkSocial v)) { SystemTo(v, "Du wurdest aus der Gruppe entfernt."); SendParty(v, null); }
            if (rest != null) { SystemToParty(rest, $"{vName} wurde aus der Gruppe entfernt.", null); PushPartyNow(rest); }
            else { SystemTo(this, $"{vName} wurde entfernt – die Gruppe ist aufgeloest."); SendParty(this, null); }
        }

        /// <summary>21.10. (MEHRSPIELER-04): Server – ist dieses Profil gerade online (fuer die Leitungsuebergabe beim Verlassen)?</summary>
        private static bool IsOnline(string profile) => profile != null && ByProfile.TryGetValue(profile, out NetworkSocial s) && s != null;

        /// <summary>Nach Aufloesen: allen frueheren Mitgliedern leere Gruppe schicken (beim naechsten Takt).</summary>
        private void NotifyDissolved()
        {
            foreach (NetworkSocial s in ByProfile.Values)
                if (s != null && PartyService.Of(s._profile) == null && s._lastParty != null && s._lastParty.Length > 0) SendParty(s, null);
        }

        private static void PushPartyNow(PartyService.Party p)
        {
            foreach (string m in p.Members)
                if (ByProfile.TryGetValue(m, out NetworkSocial s) && s != null) SendParty(s, p);
        }

        private static void SendParty(NetworkSocial s, PartyService.Party p)
        {
            string enc = p != null ? Encode(p, s._profile) : "";
            if (enc == s._lastParty) return;
            s._lastParty = enc;
            if (s.Owner != null && s.Owner.IsActive) s.TargetParty(s.Owner, enc);
        }

        private static string Encode(PartyService.Party p, string viewer)
        {
            var sb = new StringBuilder();
            foreach (string m in p.Members)
            {
                if (sb.Length > 0) sb.Append(';');
                Known.TryGetValue(m, out var k);
                bool online = ByProfile.TryGetValue(m, out NetworkSocial s) && s != null;
                float hp = 0f;
                bool alive = true;
                int level = k.level;
                if (online)
                {
                    if (s._health != null) { hp = s._health.Fraction; alive = s._health.IsAlive; }
                    if (s._inv.ServerModel != null) level = s._inv.ServerModel.Level;
                }
                sb.Append(k.name).Append('|').Append(level).Append('|').Append(k.cls).Append('|')
                  .Append(Mathf.RoundToInt(hp * 100f)).Append('|').Append(alive ? 1 : 0).Append('|')
                  .Append(online ? 1 : 0).Append('|').Append(m == p.Leader ? 1 : 0).Append('|').Append(m == viewer ? 1 : 0);
            }
            return sb.ToString();
        }

        private void Update()
        {
            if (IsServerInitialized && _profile != null)
            {
                _partyTimer -= Time.unscaledDeltaTime;
                if (_partyTimer <= 0f)
                {
                    _partyTimer = 0.25f;
                    Remember();
                    PartyService.Party p = PartyService.Of(_profile);
                    if (p != null || !string.IsNullOrEmpty(_lastParty)) SendParty(this, p);
                }
            }
            if (_identity != null && _player != null)
            {
                _identity.DisplayName = _player.DisplayName;
                _identity.IsLocal = IsOwner;
            }
        }

        // ================================================================== Server -> Besitzer

        [TargetRpc]
        private void TargetChat(NetworkConnection conn, byte channel, string from, string text)
        {
            _cChat.Add(new ChatLine { Channel = (ChatChannel)channel, From = from, Text = text, Time = Time.unscaledTime });
            while (_cChat.Count > MaxChatLines) _cChat.RemoveAt(0);
            _cRev++;
            SocialClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetInvite(NetworkConnection conn, string from)
        {
            _cInvite = string.IsNullOrEmpty(from) ? null : from;
            _cRev++;
            SocialClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetParty(NetworkConnection conn, string enc)
        {
            _cParty.Clear();
            if (!string.IsNullOrEmpty(enc))
            {
                foreach (string part in enc.Split(';'))
                {
                    string[] f = part.Split('|');
                    if (f.Length < 8) continue;
                    int.TryParse(f[1], out int lvl);
                    int.TryParse(f[3], out int hp);
                    _cParty.Add(new PartyMemberView
                    {
                        Name = f[0], Level = lvl, ClassName = f[2], Health = hp / 100f,
                        Alive = f[4] == "1", Online = f[5] == "1", Leader = f[6] == "1", Self = f[7] == "1"
                    });
                }
            }
            _cRev++;
            SocialClient.RaiseChanged();
        }

        // ================================================================== ISocialService (Client)

        public bool Ready => IsClientInitialized && IsOwner;
        public string MyName => _player != null ? _player.DisplayName : "";
        public IReadOnlyList<PartyMemberView> Party => _cParty;
        public IReadOnlyList<ChatLine> Chat => _cChat;
        public string PendingInvite => _cInvite;

        public void Say(ChatChannel channel, string target, string text)
        {
            if (!Ready) return;
            text = SocialClient.Clean(text);
            if (text.Length > 0) ServerChat((byte)channel, target ?? "", text);
        }

        public void Invite(string name) { if (Ready && !string.IsNullOrWhiteSpace(name)) ServerInvite(name.Trim()); }
        public void RespondInvite(bool accept) { if (Ready && _cInvite != null) ServerRespond(accept); }
        public void LeaveParty() { if (Ready) ServerLeave(); }
        public void Kick(string name) { if (Ready && !string.IsNullOrWhiteSpace(name)) ServerKick(name.Trim()); }

        /// <summary>Test: Chat-Zeilen als Text.</summary>
        public string DescribeChat(int last)
        {
            var sb = new StringBuilder();
            for (int i = Mathf.Max(0, _cChat.Count - last); i < _cChat.Count; i++)
                sb.Append((int)_cChat[i].Channel).Append(':').Append(_cChat[i].From).Append(':').Append(_cChat[i].Text).Append('\n');
            return sb.ToString();
        }
    }
}
