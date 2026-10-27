using System;
using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>Wo Charaktere liegen. Heute JSON-Dateien auf dem Server, spaeter eine Datenbank hinter derselben Schnittstelle.</summary>
    public interface IPersistenceService
    {
        bool TryLoad(string profileId, out CharacterSaveData data);
        void Save(CharacterSaveData data);
        string Describe(string profileId);
    }

    /// <summary>
    /// JSON-Dateien, atomar geschrieben (erst temporaere Datei, dann Austausch) – ein Absturz mitten im
    /// Schreiben hinterlaesst nie eine halbe Datei. Die vorige Fassung bleibt als .bak erhalten.
    /// CORE-005 (03.10.): Das Schreiben laeuft in einem eigenen Hintergrund-Faden. Der Hauptfaden wandelt nur
    /// in Text um (Momentaufnahme) und reiht ein; mehrere Speicherungen desselben Profils vor dem Schreiben
    /// werden zusammengefasst (die neueste gewinnt). Laden sieht immer die neueste eingereihte Fassung.
    /// Beim Beenden wird gewartet, bis alles geschrieben ist (Flush).
    /// </summary>
    public sealed class JsonFilePersistence : IPersistenceService
    {
        private readonly string _root;
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>();
        /// <summary>UI-204: Ergebnis der letzten Schreibung je Profil (true = gelungen).</summary>
        private readonly Dictionary<string, bool> _lastOk = new Dictionary<string, bool>();
        private readonly System.Threading.AutoResetEvent _signal = new System.Threading.AutoResetEvent(false);
        private System.Threading.Thread _thread;
        private string _writingId, _writingJson;
        private volatile bool _stop;

        /// <summary>false = sofort im aufrufenden Faden schreiben (alter Weg, fuer Vergleichsmessungen).</summary>
        public bool Background = true;

        // Messwerte (Lasttest-Bericht)
        public long Writes, Coalesced, Failures;
        public double MaxWriteMs, TotalWriteMs, MaxEnqueueMs;

        public JsonFilePersistence(string root)
        {
            _root = root;
            Directory.CreateDirectory(_root);
        }

        public string PathOf(string profileId) => Path.Combine(_root, profileId + ".json");
        public string Describe(string profileId) => PathOf(profileId);

        public int PendingCount { get { lock (_lock) return _pending.Count + (_writingId != null ? 1 : 0); } }

        /// <summary>UI-204 „Speichern und beenden“: Steht fuer dieses Profil noch eine Speicherung aus (eingereiht oder beim Schreiben)?</summary>
        public bool IsPending(string profileId)
        {
            lock (_lock) return _pending.ContainsKey(profileId) || _writingId == profileId;
        }

        /// <summary>UI-204: Ist die letzte Schreibung dieses Profils gelungen? (true, solange keine fehlschlug)</summary>
        public bool LastWriteOk(string profileId)
        {
            lock (_lock) return !_lastOk.TryGetValue(profileId, out bool ok) || ok;
        }

        public bool TryLoad(string profileId, out CharacterSaveData data)
        {
            data = null;
            string queued = null;
            lock (_lock)
            {
                if (!_pending.TryGetValue(profileId, out queued) && _writingId == profileId) queued = _writingJson;
            }
            if (queued != null)
            {
                data = JsonUtility.FromJson<CharacterSaveData>(queued);
                if (data != null) return true;
            }
            string path = PathOf(profileId);
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    data = JsonUtility.FromJson<CharacterSaveData>(File.ReadAllText(candidate));
                    if (data != null) return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Ashveil] Speicherstand '{candidate}' ist beschaedigt: {e.Message}");
                }
            }
            return false;
        }

        public void Save(CharacterSaveData data)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            data.savedAtUtc = DateTime.UtcNow.ToString("o");
            string json = JsonUtility.ToJson(data, false); // Momentaufnahme im Hauptfaden – ohne Einrueckung (CORE-005: kleiner und schneller)
            if (!Background)
            {
                WriteFile(data.profileId, json);
                return;
            }
            lock (_lock)
            {
                if (_pending.ContainsKey(data.profileId)) Coalesced++;
                _pending[data.profileId] = json;
                if (_thread == null || !_thread.IsAlive)
                {
                    _thread = new System.Threading.Thread(WriterLoop) { IsBackground = true, Name = "Ashveil-Speichern" };
                    _thread.Start();
                }
            }
            _signal.Set();
            double ms = sw.Elapsed.TotalMilliseconds;
            if (ms > MaxEnqueueMs) MaxEnqueueMs = ms;
        }

        /// <summary>Wartet, bis alle eingereihten Speicherungen geschrieben sind. true = alles geschrieben.</summary>
        public bool Flush(int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (PendingCount > 0)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) return false;
                _signal.Set();
                System.Threading.Thread.Sleep(2);
            }
            return true;
        }

        /// <summary>Alles schreiben und den Faden beenden (Programmende, Verlassen des Play Mode).</summary>
        public bool Shutdown(int timeoutMs)
        {
            bool ok = Flush(timeoutMs);
            _stop = true;
            _signal.Set();
            if (_thread != null && _thread.IsAlive) _thread.Join(Math.Max(100, timeoutMs / 10));
            _stop = false; // ein spaeteres Save startet den Faden neu
            return ok;
        }

        private void WriterLoop()
        {
            while (true)
            {
                string id = null, json = null;
                lock (_lock)
                {
                    foreach (var kv in _pending) { id = kv.Key; json = kv.Value; break; }
                    if (id != null)
                    {
                        _pending.Remove(id);
                        _writingId = id;
                        _writingJson = json;
                    }
                }
                if (id == null)
                {
                    if (_stop) return; // Beenden: alles geschrieben, Faden endet (kein haengender Faden beim Domain-Reload)
                    _signal.WaitOne(500);
                    continue;
                }
                WriteFile(id, json);
                lock (_lock) { _writingId = null; _writingJson = null; }
            }
        }

        private void WriteFile(string profileId, string json)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string path = PathOf(profileId);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                Writes++;
                lock (_lock) _lastOk[profileId] = true;
            }
            catch (Exception e)
            {
                Failures++;
                lock (_lock) _lastOk[profileId] = false;
                Debug.LogError($"[Ashveil] Speichern von '{profileId}' fehlgeschlagen: {e.Message}");
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            TotalWriteMs += ms;
            if (ms > MaxWriteMs) MaxWriteMs = ms;
        }

        public string Stats() =>
            $"SPEICHERN;schreibvorgaenge={Writes};zusammengefasst={Coalesced};fehler={Failures};" +
            $"schreib_ms_mittel={(Writes > 0 ? TotalWriteMs / Writes : 0):0.00};schreib_ms_max={MaxWriteMs:0.0};" +
            $"hauptfaden_ms_max={MaxEnqueueMs:0.00};hintergrund={(Background ? 1 : 0)}";
    }

    /// <summary>Server: Charaktere laden, neu anlegen und speichern.</summary>
    public static class CharacterStore
    {
        private static IPersistenceService _service;
        private static readonly HashSet<string> SeenThisSession = new HashSet<string>();

        /// <summary>Play Mode ohne Domain-Reload: jede Play-Sitzung ist ein neuer Serverstart (Test-Profile wieder frisch).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => SeenThisSession.Clear();

        /// <summary>Wartet, bis alle Speicherungen geschrieben sind (Beenden, Tests). true = vollstaendig.</summary>
        public static bool FlushSaves(int timeoutMs = 10000) =>
            !(_service is JsonFilePersistence j) || j.Flush(timeoutMs);

        /// <summary>UI-204 „Speichern und beenden“: Wird dieses Profil gerade noch geschrieben? (ohne zu warten)</summary>
        public static bool IsSaving(string profile) =>
            _service is JsonFilePersistence j && !string.IsNullOrEmpty(profile) && j.IsPending(profile);

        /// <summary>UI-204: Ist die letzte Speicherung dieses Profils gelungen?</summary>
        public static bool LastSaveOk(string profile) =>
            !(_service is JsonFilePersistence j) || string.IsNullOrEmpty(profile) || j.LastWriteOk(profile);

        /// <summary>Beim Beenden des Programms (und beim Verlassen des Play Mode) nichts verlieren.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void HookQuit()
        {
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            bool ok = !(_service is JsonFilePersistence j) || j.Shutdown(10000);
            if (!ok) Debug.LogError("[Ashveil] Beim Beenden konnten nicht alle Speicherstaende geschrieben werden (10 s).");
        }

        public static IPersistenceService Service
        {
            get
            {
                if (_service == null) _service = new JsonFilePersistence(SavesRoot);
                return _service;
            }
            set => _service = value;
        }

        /// <summary>Editor: &lt;Projekt&gt;/Saves/Characters · Build: &lt;Programmordner&gt;/Saves/Characters</summary>
        public static string SavesRoot => Ashveil.Core.ProjectPaths.Saves("Characters");

        /// <summary>Test-Profile (selftest-*, duo-*) beginnen pro Serverstart einmal frisch.</summary>
        /// <summary>
        /// Beginnt dieses Profil an der gespeicherten Weltposition? Test-Profile nicht (sie starten fuer die Regression am
        /// Wachtfeuer) – ausser den Welt-Tests ("duo-welt…"), die genau das pruefen.
        /// </summary>
        public static bool RestoresPosition(string profile) =>
            !IsTestProfile(profile) || profile.StartsWith("duo-welt", StringComparison.Ordinal);

        public static bool IsTestProfile(string profile) =>
            profile.StartsWith("selftest", StringComparison.Ordinal) || profile.StartsWith("duo-", StringComparison.Ordinal) ||
            profile.StartsWith("fresh", StringComparison.Ordinal) || profile.StartsWith("social-", StringComparison.Ordinal) ||
            profile.StartsWith("kareth-", StringComparison.Ordinal) || profile.StartsWith("group-", StringComparison.Ordinal) ||
            profile.StartsWith("mine-", StringComparison.Ordinal) || profile.StartsWith("bar-", StringComparison.Ordinal) ||
            profile.StartsWith("creator-", StringComparison.Ordinal) || profile.StartsWith("emote-", StringComparison.Ordinal) ||
            profile.StartsWith("arena-", StringComparison.Ordinal) || profile.StartsWith("horse-", StringComparison.Ordinal);

        // ================================================================== M7: Charakter-Editor

        /// <summary>
        /// Braucht dieses Profil den Charakter-Editor? Ja, wenn es noch keinen Charakter hat – ausser Test-Profilen
        /// (legen sich selbst an), der festen Besetzung (Maxmod, Krisbot, Maltebot) und "-ashveil-autocreate".
        /// "creator-*" = Test des Editors: beginnt pro Serverstart ohne Charakter.
        /// </summary>
        public static bool NeedsCreation(string profile)
        {
            if (string.IsNullOrEmpty(profile) || ProfileSelection.HasArg("-ashveil-autocreate")) return false;
            // NET-012: Lasttest-Bots (bot-NN) legen sich selbst an. Das Argument -ashveil-autocreate haben nur die Bots,
            // entschieden wird aber am Server – ohne diese Zeile stand jeder Bot unsichtbar im Charakter-Editor.
            // Bewusst KEIN Test-Profil (keine Test-Befehle fuer "bot-").
            if (profile.StartsWith("bot-", StringComparison.Ordinal)) return false;
            bool editorTest = profile.StartsWith("creator-", StringComparison.Ordinal);
            if (TryCast(profile, out _, out _) || (!editorTest && IsTestProfile(profile))) return false;
            if (editorTest && !SeenThisSession.Contains(profile)) return true;
            return !Service.TryLoad(profile, out _);
        }

        private static Dictionary<string, string> _names; // kleingeschriebener Name -> Profil

        private static Dictionary<string, string> Names
        {
            get
            {
                if (_names != null) return _names;
                _names = new Dictionary<string, string>();
                foreach (var c in Cast) _names[c.Value.name.ToLowerInvariant()] = c.Key;
                try
                {
                    if (Directory.Exists(SavesRoot))
                        foreach (string f in Directory.GetFiles(SavesRoot, "*.json"))
                        {
                            try
                            {
                                var d = JsonUtility.FromJson<CharacterSaveData>(File.ReadAllText(f));
                                if (d != null && !string.IsNullOrEmpty(d.name) && !string.IsNullOrEmpty(d.profileId) && !IsTestProfile(d.profileId))
                                    _names[d.name.ToLowerInvariant()] = d.profileId;
                            }
                            catch (Exception) { /* beschaedigte Datei: wird beim Laden gemeldet */ }
                        }
                }
                catch (Exception e) { Debug.LogWarning("[Ashveil] Namensliste nicht lesbar: " + e.Message); }
                return _names;
            }
        }

        /// <summary>Ist der Name schon vergeben (von einem anderen Profil oder der festen Besetzung)?</summary>
        public static bool IsNameTaken(string name, string exceptProfile) =>
            !string.IsNullOrEmpty(name) && Names.TryGetValue(name.ToLowerInvariant(), out string owner) && owner != exceptProfile;

        /// <summary>Server: Name pruefen. null = in Ordnung (clean = vereinheitlichte Schreibweise).</summary>
        public static string ValidateNewName(string raw, string profile, out string clean)
        {
            clean = Ashveil.Data.CharacterNameRules.Normalize(raw);
            string err = Ashveil.Data.CharacterNameRules.Validate(clean);
            if (err != null) return err;
            if (IsNameTaken(clean, profile)) return $"„{clean}“ ist schon vergeben.";
            return null;
        }

        /// <summary>Server: neuen Charakter mit Name und Aussehen aus dem Editor anlegen und speichern.</summary>
        public static CharacterSaveData CreateFromEditor(string profile, string requestedClass, string name, CharacterAppearanceData look)
        {
            CharacterClassDefinition cls = GameData.Class(requestedClass) ?? GameDataBootstrap.DefaultClass;
            CharacterSaveData d = CreateNew(profile, cls);
            d.name = name;
            look.EnsureArrays();
            for (int i = 0; i < look.dyes.Length; i++) look.dyes[i] = default; // Neuling: keine Faerbungen/Uebertragungen
            for (int i = 0; i < look.transmog.Length; i++) look.transmog[i] = null;
            look.hideHelm = look.hideCloak = false;
            d.race = Ashveil.Data.RaceCatalog.At(look.race).id;
            look.race = Ashveil.Data.RaceCatalog.IndexOf(d.race);
            d.appearance = look.Clone();
            SeenThisSession.Add(profile);
            Service.Save(d);
            Names[name.ToLowerInvariant()] = profile;
            return d;
        }

        public static bool IsValidProfileId(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length > 48) return false;
            foreach (char c in p)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }

        public static CharacterSaveData LoadOrCreate(string profile, string requestedClass, out bool created)
        {
            created = false;
            bool firstThisSession = SeenThisSession.Add(profile);
            if (!(IsTestProfile(profile) && firstThisSession) && Service.TryLoad(profile, out CharacterSaveData data))
            {
                Sanitize(data);
                return data;
            }
            created = true;
            CharacterClassDefinition cls = GameData.Class(requestedClass) ?? GameDataBootstrap.DefaultClass;
            CharacterSaveData fresh = CreateNew(profile, cls);
            Service.Save(fresh);
            return fresh;
        }

        /// <summary>Neuer Charakter mit der Startausruestung seiner Klasse.</summary>
        public static CharacterSaveData CreateNew(string profile, CharacterClassDefinition cls)
        {
            var model = new InventoryModel { Class = cls, Level = StartLevelFor(profile) };
            if (cls != null)
            {
                foreach (ItemDefinition def in cls.startEquipment)
                {
                    if (def == null) continue;
                    int before = model.FirstFreeBagSlot();
                    if (model.Add(new ItemStack(def.Id, 1)) > 0 && before >= 0)
                    {
                        InvResult r = model.AutoEquip(before);
                        if (!r.Ok) Debug.LogWarning($"[Ashveil] Startausruestung '{def.Id}': {r.Error}");
                    }
                }
                foreach (ItemAmount a in cls.startBag)
                    if (a.item != null) model.Add(new ItemStack(a.item.Id, Mathf.Max(1, a.count)));
                // Munitionsplatz: passende Startmunition gleich waehlen
                SelectAmmo(model);
            }
            TryCast(profile, out _, out int body);
            return new CharacterSaveData
            {
                appearance = DefaultAppearance(body),
                actionBar = DefaultBarFor(cls),
                profileId = profile,
                name = NameFor(profile),
                classId = cls != null ? cls.Id : "",
                level = StartLevelFor(profile),
                money = Ashveil.Data.Progression.StartMoney,
                bag = (ItemStack[])model.Bag.Clone(),
                equipment = (ItemStack[])model.Equipment.Clone(),
                containers = (ItemStack[])model.Containers.Clone(),
                ammoId = model.AmmoId
            };
        }

        /// <summary>Startleiste: die Hechtrolle nur fuer Klassen, die rollen koennen (Kampfumbau 30.09.); Klassenfaehigkeiten legt der Server beim Anmelden dazu.</summary>
        public static List<ActionBarEntry> DefaultBarFor(CharacterClassDefinition cls)
        {
            List<ActionBarEntry> bar = ActionBarLayout.Defaults();
            if (cls != null && !cls.canRoll) bar.RemoveAll(e => e.referenceId == ActionBarLayout.Dodge);
            return bar;
        }

        /// <summary>Erstes Aussehen: gewaehlter Koerper, erste passende Frisur, dunkle Haarfarbe.</summary>
        public static CharacterAppearanceData DefaultAppearance(int body)
        {
            var d = new CharacterAppearanceData { body = body, hairColor = 1 };
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat != null) d.hair = NetworkAppearance.FirstStyle(cat.hairstyles, cat, body);
            return d;
        }

        /// <summary>Neue Charaktere beginnen auf Stufe 1. Test-Profile der Item-Tests (selftest*, duo-*) auf Stufe 5,
        /// damit ihre Pruefungen unabhaengig von der Stufenentwicklung bleiben; "fresh-*" = echter Neuling.</summary>
        public static int StartLevelFor(string profile) =>
            profile == null || !IsTestProfile(profile) || profile.StartsWith("fresh", StringComparison.Ordinal) ? 1 :
            profile.StartsWith("mine-", StringComparison.Ordinal) ? 12 :
            profile.StartsWith("arena-", StringComparison.Ordinal) ? 15 : 5; // Salzgruben-Test: Stufe 10–14, Kolosseum 14–16

        /// <summary>
        /// Feste Besetzung (Wunsch von Max, 26.09.): eigene Spielfigur "Maxmod", maennlicher Mitspieler "Maltebot",
        /// weibliche Testfigur "Krisbot" (Koerper 1). Tests laufen immer mit Krisbot als zweiter Figur.
        /// </summary>
        private static readonly Dictionary<string, (string name, int body)> Cast = new Dictionary<string, (string, int)>
        {
            { "editor", ("Maxmod", 0) },
            { "maltebot", ("Maltebot", 0) },
            { "selftest-editor", ("Maxmod", 0) }, { "selftest-client", ("Krisbot", 1) },
            { "duo-a", ("Maxmod", 0) }, { "duo-b", ("Krisbot", 1) },
            { "fresh-a", ("Maxmod", 0) }, { "fresh-b", ("Krisbot", 1) },
            { "kareth-a", ("Maxmod", 0) }, { "kareth-b", ("Krisbot", 1) },
            { "social-a", ("Maxmod", 0) }, { "social-b", ("Krisbot", 1) }, { "social-c", ("Maltebot", 0) },
            { "group-a", ("Maxmod", 0) }, { "group-b", ("Krisbot", 1) }, { "group-c", ("Maltebot", 0) },
            { "mine-a", ("Maxmod", 0) }, { "mine-b", ("Krisbot", 1) }, { "mine-c", ("Maltebot", 0) },
            { "bar-a", ("Maxmod", 0) }, { "bar-b", ("Krisbot", 1) },
            { "creator-a", ("Maxmod", 0) },
            { "emote-a", ("Maxmod", 0) }, { "emote-b", ("Krisbot", 1) }, { "emote-c", ("Maltebot", 0) },
            { "arena-a", ("Maxmod", 0) }, { "arena-b", ("Krisbot", 1) }, { "arena-c", ("Maltebot", 0) },
            { "horse-a", ("Maxmod", 0) }, { "horse-b", ("Hallbot", 0) },
            { "duo-glocke", ("Maxmod", 0) }
        };

        public static bool TryCast(string profile, out string name, out int body)
        {
            if (profile != null && Cast.TryGetValue(profile, out var c)) { name = c.name; body = c.body; return true; }
            name = null;
            body = 0;
            return false;
        }

        private static readonly string[] NameStart = { "Al", "Bra", "Cor", "Dar", "El", "Fen", "Gar", "Hal", "Is", "Jor", "Kel", "Lur", "Mar", "Nor", "Os", "Per", "Ral", "Sev", "Tor", "Ul", "Var", "Wen" };
        private static readonly string[] NameEnd = { "dric", "wen", "ric", "mar", "la", "ven", "dor", "ith", "gar", "rin", "ana", "ek", "lin", "mund", "wyn", "ra" };

        /// <summary>Fester Name aus der Profil-ID (gleiches Profil = gleicher Name), bis der Charakter-Editor kommt.</summary>
        public static string NameFor(string profile)
        {
            if (TryCast(profile, out string castName, out _)) return castName;
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in profile ?? "") h = (h ^ c) * 16777619;
                return NameStart[h % (uint)NameStart.Length] + NameEnd[(h / 31) % (uint)NameEnd.Length];
            }
        }

        public static bool IsValidName(string n) => Ashveil.Data.CharacterNameRules.Validate(n) == null || (n != null && TryCastName(n));

        private static bool TryCastName(string n)
        {
            foreach (var c in Cast) if (c.Value.name == n) return true;
            return false;
        }

        /// <summary>Munitionsplatz leer, aber passende Munition in den Taschen: die erste waehlen.</summary>
        public static void SelectAmmo(InventoryModel model)
        {
            if (!string.IsNullOrEmpty(model.AmmoId)) return;
            ItemDefinition ranged = model.Def(model.GetEquipped(EquipmentSlot.Ranged));
            AmmoType want = ranged != null && ranged.IsWeapon ? ranged.weapon.ammoType : AmmoType.None;
            if (want == AmmoType.None) return;
            foreach (int c in model.Cells())
            {
                ItemDefinition d = model.Def(model.Bag[c]);
                if (d != null && d.IsAmmo && d.ammoType == want) { model.Move(c, InventoryModel.AmmoCode); return; }
            }
        }

        /// <summary>Tests: einen (alten) Speicherstand so aufbereiten wie beim Laden.</summary>
        public static void MigrateForTests(CharacterSaveData d) => Sanitize(d);

        private static void Sanitize(CharacterSaveData d)
        {
            if (!IsValidName(d.name)) d.name = NameFor(d.profileId);
            if (TryCast(d.profileId, out string castName, out _)) d.name = castName;
            if (d.bag == null || d.bag.Length != InventoryModel.BagSize) Array.Resize(ref d.bag, InventoryModel.BagSize);
            if (d.equipment == null || d.equipment.Length != InventoryModel.EquipCount) Array.Resize(ref d.equipment, InventoryModel.EquipCount);
            if (d.containers == null || d.containers.Length != InventoryModel.BagSlotCount) Array.Resize(ref d.containers, InventoryModel.BagSlotCount);
            if (d.ammoId == null) d.ammoId = "";
            // v9: Fernkampfwaffen haben wie in WoW einen eigenen Platz – aus Haupt-/Nebenhand dorthin umziehen
            if (d.version < 9 && d.equipment[(int)EquipmentSlot.Ranged].IsEmpty)
                foreach (EquipmentSlot hand in new[] { EquipmentSlot.OffHand, EquipmentSlot.MainHand })
                {
                    ItemDefinition def = d.equipment[(int)hand].IsEmpty ? null : GameData.Item(d.equipment[(int)hand].id);
                    if (def == null || !def.IsRangedWeapon) continue;
                    d.equipment[(int)EquipmentSlot.Ranged] = d.equipment[(int)hand];
                    d.equipment[(int)hand] = ItemStack.Empty;
                    break;
                }
            if (d.professions == null) d.professions = new List<ProfessionSkill>();
            if (d.knownRecipes == null) d.knownRecipes = new List<string>();
            if (d.appearance == null) d.appearance = DefaultAppearance(0);
            d.appearance.EnsureArrays();
            if (d.unlockedAppearances == null) d.unlockedAppearances = new List<string>();
            if (d.level < 1) d.level = 1;
            if (d.level > Ashveil.Data.Progression.GlobalMaxLevel) d.level = Ashveil.Data.Progression.GlobalMaxLevel;
            if (d.quests == null) d.quests = new List<QuestSave>();
            if (d.completedQuests == null) d.completedQuests = new List<string>();
            // 22.10.2026 (Runde 6, LADEN-01 nach Codex-Gegenprüfung): laufende Aufträge VOR dem Verwerfen unbekannter merken – die
            // Lade-Bereinigung unten schützt deren Questgegenstände auch dann, wenn die Auftragsdefinition fehlt.
            var laufendVorBereinigung = new List<string>();
            foreach (QuestSave q in d.quests)
                if (q != null && !string.IsNullOrEmpty(q.id)) laufendVorBereinigung.Add(q.id);
            d.quests.RemoveAll(q => q == null || GameData.Quest(q.id) == null);
            foreach (QuestSave q in d.quests)
            {
                int n = GameData.Quest(q.id).objectives.Count;
                if (q.progress == null || q.progress.Length != n) Array.Resize(ref q.progress, n);
            }
            if (d.xp < 0) d.xp = 0;
            if (d.money < 0) d.money = 0;
            // v3 (M6.6): Actionbar – alte Staende bekommen die Standardbelegung (entspricht den frueheren festen Tasten)
            if (d.version < 3 || d.actionBar == null) d.actionBar = ActionBarLayout.Defaults();
            d.actionBar = ActionBarLayout.Sanitize(d.actionBar);
            if (d.version < 2) d.money = Math.Max(d.money, Ashveil.Data.Progression.StartMoney);
            if (d.mastery == null) d.mastery = new List<MasterySave>();
            d.mastery.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id) || double.IsNaN(m.points) || m.points < 0);
            if (d.lifetime == null) d.lifetime = new LifetimeStats();
            // v10: Klassenfaehigkeiten und Talente – unbekannte oder fremde Eintraege verwerfen
            if (d.spells == null) d.spells = new List<SpellSave>();
            d.spells.RemoveAll(sp => sp == null || sp.rank <= 0 || GameData.Get<SpellDefinition>(sp.id) == null ||
                                     (!string.IsNullOrEmpty(d.classId) && GameData.Get<SpellDefinition>(sp.id).classId != d.classId));
            foreach (SpellSave sp in d.spells) sp.rank = Math.Min(sp.rank, GameData.Get<SpellDefinition>(sp.id).RankCount);
            if (d.talents == null) d.talents = new List<TalentSave>();
            d.talents.RemoveAll(t => t == null || t.rank <= 0 || GameData.Get<TalentDefinition>(t.id) == null ||
                                     (!string.IsNullOrEmpty(d.classId) && GameData.Get<TalentDefinition>(t.id).classId != d.classId));
            foreach (TalentSave t in d.talents) t.rank = Math.Min(t.rank, GameData.Get<TalentDefinition>(t.id).maxRank);
            if (d.talentResets < 0) d.talentResets = 0;
            // Reittier: unbekannte Art oder falsche Klasse verwerfen (Klassenwechsel, entfernte Inhalte)
            if (d.mount != null && (string.IsNullOrEmpty(d.mount.id) || Ashveil.Data.MountCatalog.Get(d.mount.id) == null)) d.mount = null;
            if (d.companion != null && Ashveil.Data.CompanionCatalog.Get(d.companion.species) == null) d.companion = null;
            if (d.companion != null && d.companion.name != null && d.companion.name.Length > 24) d.companion.name = d.companion.name.Substring(0, 24);
            if (d.mount != null && d.mount.name != null && d.mount.name.Length > 24) d.mount.name = d.mount.name.Substring(0, 24);
            // v8: Volk (alte Staende = Mensch; das Aussehen folgt dem gespeicherten Volk), Geschichtsmarken, Pferde-Geschichte
            d.race = Ashveil.Data.RaceCatalog.Normalize(d.race);
            d.appearance.race = Ashveil.Data.RaceCatalog.IndexOf(d.race);
            if (d.storyFlags == null) d.storyFlags = new List<string>();
            d.storyFlags.RemoveAll(string.IsNullOrEmpty);
            if (d.horseStory == null) d.horseStory = new HorseStorySave();
            if (d.horseStory.state < 0 || d.horseStory.state > (int)Ashveil.Data.HorseStoryState.BondedToPlayer) d.horseStory.state = 0;
            if (float.IsNaN(d.horseStory.herdRouteProgress) || d.horseStory.herdRouteProgress < 0f) d.horseStory.herdRouteProgress = 0f;
            StoryMilestones.Derive(d);
            // v11: entdeckte Orte (Entdeckungs-EP) – alte Staende beginnen leer
            if (d.discoveredAreas == null) d.discoveredAreas = new List<string>();
            d.discoveredAreas.RemoveAll(string.IsNullOrEmpty);
            if (d.version < CharacterSaveData.CurrentVersion) d.version = CharacterSaveData.CurrentVersion;
            // Unbekannte IDs (Gegenstand aus den Daten entfernt) bleiben erhalten, werden aber gemeldet
            foreach (ItemStack s in d.bag)
                if (!s.IsEmpty && GameData.Item(s.id) == null) Debug.LogWarning($"[Ashveil] Profil '{d.profileId}': unbekannter Gegenstand '{s.id}'.");
            // 22.10.2026 (Runde 5, LADEN-01): Questgegenstände ohne Bedarf aus alten Spielständen entfernen (eine Zeile im Log)
            if (GameData.Ready)
            {
                List<string> weg = QuestgegenstaendeAufraeumen(d, GameData.Item, GameData.Quest, GameData.Quests, laufendVorBereinigung);
                if (weg.Count > 0)
                    Debug.Log($"[Ashveil] Profil '{d.profileId}': {weg.Count} nicht mehr benötigte Questgegenstand-Stapel beim Laden entfernt (LADEN-01): {string.Join(", ", weg)}");
            }
        }

        /// <summary>
        /// 22.10.2026 (Runde 5, LADEN-01): Defensive Lade-Bereinigung für alte Spielstände. Seit 21.10. gilt Max' Regel „Questgegenstände
        /// nur, solange sie gebraucht werden“ (Abgabe entfernt alle Reste, Beute nur bei Bedarf) – ältere Stände tragen aber noch
        /// Glockensplitter/Verblichene Briefe (fielen ohne Auftrag) und Überschuss längst abgegebener Aufträge; die sind unverkäuflich und
        /// nicht wegzuwerfen. Entfernt wird ein Stapel nur, wenn ALLES gilt:
        ///   · Kategorie Quest mit gesetzter questId (andere Kategorien und unbekannte IDs bleiben unberührt),
        ///   · die questId hat keine Auftragsdefinition (seit Runde 6 nur noch für die festen Altlasten, siehe unten) ODER ihr Auftrag
        ///     ist abgeschlossen (completedQuests; Aufträge sind nicht wiederholbar – QuestRules.CanAccept lehnt Erledigte ab),
        ///   · kein noch offener Auftrag (laufend, aufgegeben oder noch nicht angenommen) nennt den Gegenstand als Sammelziel oder
        ///     Startgegenstand (z. B. „Zollbuch“: questId w19, aber Startgegenstand von w20).
        /// Laufende und aufgegebene Aufträge behalten ihre Gegenstände (Max: „beim Aufgeben behalten“), denn sie sind nicht abgeschlossen.
        /// Durchsucht alle Rucksack- und Taschenzellen (bag); Taschenplätze (containers) und Ausrüstung tragen nie Questgegenstände.
        /// Wirkt bei jedem Laden und ist idempotent (keine Versionsstufe nötig). Rückgabe: entfernte Stapel als „id ×n (Grund)“.
        /// 22.10.2026 (Runde 6, LADEN-01 nach Codex-Gegenprüfung REQ-20261022-CODEX-LADEBEREINIGUNG-REVIEW): konservativer.
        /// Eine FEHLENDE Auftragsdefinition belegt keinen nutzlosen Gegenstand mehr (GameData.Ready heißt nur „Datenbank da“, nicht
        /// „jede Definition vollständig“) – solche Stapel bleiben. Prüfreihenfolge je Stapel:
        ///   1. Auftrag läuft (d.quests bzw. <paramref name="laufendVorBereinigung"/>: der Stand vor dem Verwerfen unbekannter
        ///      Aufträge im Ladeweg) → bleibt, auch ohne Definition und auch, wenn er zugleich als erledigt geführt wird.
        ///   2. Auftragsdefinition fehlt → bleibt; einzige Ausnahme die festen Altlasten <see cref="AltlastIds"/> (Glockensplitter,
        ///      Verblichener Brief: fielen bis 21.10. ohne Auftrag als Beute).
        ///   3. Auftrag vorhanden, aber nicht abgeschlossen (aufgegeben, noch nicht angenommen) → bleibt.
        ///   4. Ein noch offener Auftrag braucht ihn (Sammelziel/Startgegenstand) → bleibt.
        /// Ohne Gesamtliste der Aufträge (<paramref name="alleAuftraege"/> null) oder mit einem laufenden Auftrag ohne Definition
        /// (dessen Bedarf unbekannt ist) lässt sich Schritt 4 nicht belegen → nichts entfernen.
        /// </summary>
        public static List<string> QuestgegenstaendeAufraeumen(CharacterSaveData d, Func<string, ItemDefinition> item,
            Func<string, QuestDefinition> quest, IReadOnlyList<QuestDefinition> alleAuftraege, IEnumerable<string> laufendVorBereinigung = null)
        {
            var weg = new List<string>();
            if (d == null || d.bag == null || item == null || quest == null) return weg;
            // 22.10.2026 (Runde 6, LADEN-01): ohne Gesamtliste kann die Bedarfsprüfung für Folgeaufträge nichts beweisen → erhalten
            if (alleAuftraege == null) return weg;
            var laufend = new HashSet<string>();
            if (d.quests != null)
                foreach (QuestSave q in d.quests)
                    if (q != null && !string.IsNullOrEmpty(q.id)) laufend.Add(q.id);
            if (laufendVorBereinigung != null)
                foreach (string id in laufendVorBereinigung)
                    if (!string.IsNullOrEmpty(id)) laufend.Add(id);
            // 22.10.2026 (Runde 6, LADEN-01): läuft ein Auftrag ohne Definition, ist sein Bedarf unbekannt → nichts entfernen
            foreach (string id in laufend)
                if (quest(id) == null) return weg;
            var erledigt = new HashSet<string>(d.completedQuests ?? new List<string>());
            erledigt.ExceptWith(laufend);   // läuft er (beschädigter Stand: auch als erledigt geführt), gilt er als offen
            HashSet<string> nochGebraucht = null;   // erst bei Bedarf: Sammelziele/Startgegenstände nicht abgeschlossener Aufträge
            for (int i = 0; i < d.bag.Length; i++)
            {
                ItemStack s = d.bag[i];
                if (s.IsEmpty) continue;
                ItemDefinition def = item(s.id);
                if (def == null || def.category != ItemCategory.Quest || string.IsNullOrEmpty(def.questId)) continue;
                if (laufend.Contains(def.questId)) continue;   // 1. laufender Auftrag: bleibt (auch ohne Definition)
                bool ohneAuftrag = quest(def.questId) == null;
                if (ohneAuftrag && Array.IndexOf(AltlastIds, s.id) < 0) continue;   // 2. fehlende Definition: bleibt (außer feste Altlast)
                if (!ohneAuftrag && !erledigt.Contains(def.questId)) continue;   // 3. aufgegeben oder noch nicht angenommen: bleibt
                if (nochGebraucht == null)
                {
                    nochGebraucht = new HashSet<string>();
                    foreach (QuestDefinition q in alleAuftraege)
                    {
                        if (q == null || erledigt.Contains(q.Id)) continue;
                        if (q.objectives != null)
                            foreach (QuestObjective o in q.objectives)
                                if (o != null && o.type == QuestObjectiveType.Collect && !string.IsNullOrEmpty(o.targetId)) nochGebraucht.Add(o.targetId);
                        if (q.startItems != null)
                            foreach (QuestItemReward r in q.startItems)
                                if (r?.item != null) nochGebraucht.Add(r.item.Id);
                    }
                }
                if (nochGebraucht.Contains(s.id)) continue;   // 4. ein offener Auftrag braucht ihn: bleibt
                weg.Add($"{s.id} ×{s.count} ({(ohneAuftrag ? "Altlast ohne Auftrag " + def.questId : "Auftrag " + def.questId + " abgeschlossen")})");
                d.bag[i] = ItemStack.Empty;
            }
            return weg;
        }

        /// <summary>
        /// 22.10.2026 (Runde 6, LADEN-01 nach Codex-Gegenprüfung): die einzigen Questgegenstände, die beim Laden OHNE Auftragsdefinition
        /// entfernt werden – Glockensplitter und Verblichener Brief fielen bis zum 21.10. als Beute, obwohl ihre Aufträge
        /// (quest.bell_shepherd, quest.faded_letter) nie angelegt wurden (ItemContentBuilder, AUFTRAEGE-03/FORTSCHRITT-04). Bekommen
        /// sie später einen Auftrag, gelten für sie die normalen Regeln.
        /// </summary>
        public static readonly string[] AltlastIds = { "item.bell_shard", "item.faded_letter" };
    }

    /// <summary>
    /// Client: welche Profil-ID dieser Spieler meldet.
    ///   Kommandozeile "-ashveil-profile name" · Editor: "editor" · Build: zufaellige ID in ashveil_profile.txt
    /// (Anmeldung mit Passwort/Konto folgt mit dem Login-Dienst.)
    /// </summary>
    public static class ProfileSelection
    {
        /// <summary>Vom Selbsttest gesetzt, bevor die Verbindung steht.</summary>
        public static string Override;

        /// <summary>Play Mode ohne Domain-Reload: Test-Profil nicht in die naechste Sitzung mitnehmen.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Override = null;

        public static string Resolve()
        {
            string arg = ArgValue("-ashveil-profile");
            if (!string.IsNullOrEmpty(arg)) return arg.ToLowerInvariant();
            if (!string.IsNullOrEmpty(Override)) return Override;
            if (Application.isEditor) return "editor";
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ashveil_profile.txt"));
            try
            {
                if (File.Exists(file))
                {
                    string id = File.ReadAllText(file).Trim();
                    if (CharacterStore.IsValidProfileId(id)) return id;
                }
                string fresh = "spieler-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                File.WriteAllText(file, fresh);
                return fresh;
            }
            catch (Exception)
            {
                return "spieler-" + SystemInfo.deviceUniqueIdentifier.GetHashCode().ToString("x8");
            }
        }

        public static string RequestedClass() => ArgValue("-ashveil-class") ?? "";

        /// <summary>M7, nur Editor: "Ashveil &gt; Charakter-Editor ausprobieren" – Play mit einem neuen Profil "neu-…".</summary>
        public const string NewCharacterFlagFile = "Temp/ashveil_newchar.flag";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ReadNewCharacterFlag()
        {
            if (!Application.isEditor || !File.Exists(NewCharacterFlagFile)) return;
            try { File.Delete(NewCharacterFlagFile); } catch (IOException) { }
            Override = "neu-" + DateTime.Now.ToString("MMddHHmmss");
            Debug.Log("[Ashveil] Neues Profil '" + Override + "' – der Charakter-Editor öffnet sich.");
        }

        public static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : string.Empty;
            }
            return null;
        }

        public static bool HasArg(string name) => ArgValue(name) != null;
    }
}
