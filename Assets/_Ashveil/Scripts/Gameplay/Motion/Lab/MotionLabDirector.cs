using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Ashveil.Gameplay.Motion.Lab
{
    /// <summary>
    /// Motion-Lab (§106–§110): eigene Szene zum Pruefen der Bewegung – ohne Netz, ohne Kareth.
    ///
    /// Zwei Betriebsarten:
    ///  • Selbst laufen: normale Steuerung, dazu ein Entwickler-Panel (Rasse, Koerper, Klasse, Ruestung, Waffe,
    ///    Zeitlupe, Anzeige des Bewegungskontexts). Taste F8 blendet das Panel ein/aus.
    ///  • Aufnahme: die Figur faehrt einen festen Parcours ab (Idle, Gehen … Sprint, Anhalten, Drehen, Hang, Treppe,
    ///    Hindernisse, Sprung). Je Abschnitt entsteht ein Kontaktbogen (Bildreihe) aus seitlicher Kamera und ein
    ///    Messbericht (Fussgleiten, Eindringen in den Boden) in Logs/Ashveil/Motion/.
    /// </summary>
    public sealed class MotionLabDirector : MonoBehaviour
    {
        public const string FlagFile = "Temp/ashveil_motionlab.flag";

        [SerializeField] private CharacterMotor puppet;
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private Camera captureCamera;
        [Tooltip("Startpunkte der Bahnen (Name = Bahn).")]
        [SerializeField] private Transform[] lanes;

        [Header("Aufnahme")]
        [SerializeField] private int tileWidth = 360;
        [SerializeField] private int tileHeight = 480;
        [SerializeField] private int columns = 6;

        private CharacterMotionOrchestrator _orchestrator;
        private CharacterBodySwitcher _bodies;
        private bool _panel = true;
        private bool _recording;
        private string _status = "";
        private readonly StringBuilder _report = new StringBuilder();

        // Messung
        private Transform _lFoot, _rFoot, _lToe, _rToe;
        private readonly FootTrack _lTrack = new FootTrack(), _rTrack = new FootTrack();

        public static string OutputDir => ProjectPaths.Logs("Motion");

        private void Start()
        {
            _bodies = puppet != null ? puppet.GetComponent<CharacterBodySwitcher>() : null;
            _orchestrator = puppet != null ? puppet.GetComponentInChildren<CharacterMotionOrchestrator>() : null;
            string flag = null;
            if (Application.isEditor && File.Exists(FlagFile))
            {
                try { flag = File.ReadAllText(FlagFile).Trim(); File.Delete(FlagFile); } catch (IOException) { }
            }
            if (!string.IsNullOrEmpty(flag)) StartCoroutine(Record(flag));
        }

        // ================================================================== Parcours

        /// <summary>Around = Rundum: jede Aufnahme von vorn, rechts, hinten und links (Zeilen) – §76/Max: „von allen Seiten“.</summary>
        private enum Shot { Side, Front, Top, ThreeQuarter, Around, Hands, Head, Kleid }   // Kleid: 21.10.2026 Nahaufnahmen der Kleidung
        private static readonly float[] AroundYaw = { 0f, 90f, 180f, 270f };
        private static readonly string[] AroundName = { "vorn", "rechts", "hinten", "links" };

        private struct Step
        {
            public string Id, Title, Lane;
            public float Duration, Lead;
            public Vector3 Offset;
            public float Yaw;
            public Shot Shot;
            public int Frames;
            public Func<float, MoveCommand> Command;
            public Action Start;
            /// <summary>Waffen-Parcours: vor dem Abschnitt anlegen (Haupthand, Nebenhand; null = leer).</summary>
            public string[] Loadout;
            /// <summary>Vorher in den Kampf (Waffe ziehen) und warten – fuer Wegsteck-Aufnahmen.</summary>
            public bool PreCombat;
            /// <summary>Anatomie: alle Clips des Animators Bild fuer Bild pruefen (keine Bewegung).</summary>
            public bool ClipAudit;
        }

        /// <summary>Waffen-Parcours: jede Ausruestung einmal. Spalten: Kennung, Titel, Haupthand, Nebenhand, Fernkampf, Klasse.
        /// Kampfumbau 30.09.: Feuerwaffen ruhen – dafuer Bogen, Stab (Zauber), zwei Dolche, zwei Schwerter.</summary>
        private static readonly string[][] Loadouts =
        {
            new[] { "a_schwert_schild", "Schwert + Schild", "item.warden_blade", "item.oak_shield", null, "class.warden" },
            new[] { "b_schwert", "Einhandschwert", "item.warden_blade", null, null, "class.warden" },
            new[] { "c_keule_schild", "Keule + Schild", "item.gravewarden_mace", "item.oak_shield", null, "class.warden" },
            new[] { "d_zweihandschwert", "Zweihandschwert", "item.bellringer", null, null, "class.warden" },
            new[] { "e_zweihandaxt", "Zweihandaxt", "item.notched_greataxe", null, null, "class.warden" },
            new[] { "f_dolch", "Dolch", "item.cinder_dagger", null, null, "class.ashstalker" },
            new[] { "g_zwei_dolche", "Zwei Dolche", "item.cinder_dagger", "item.cinder_dagger", null, "class.ashstalker" },
            new[] { "h_zwei_schwerter", "Zwei Schwerter", "item.warden_blade", "item.warden_blade", null, "class.ashstalker" },
            new[] { "i_stab", "Stab (Zauber)", "item.ember_staff", null, null, "class.emberweaver" },
            new[] { "j_bogen", "Bogen", "item.cinder_dagger", null, "item.hunters_shortbow", "class.ashstalker" },
            new[] { "k_waffenlos", "Waffenlos", null, null, null, "class.warden" },
        };

        private static string[] KitOf(string[] lo) => new[] { lo[2], lo[3], lo[4], lo[5] };
        private static bool IsBow(string[] lo) => lo[4] != null;

        private static MoveCommand Fwd(bool walk = false, bool sprint = false, bool jog = false, bool epic = false) =>
            new MoveCommand { Forward = 1f, Walk = walk, Sprint = sprint, Jog = jog, EpicWalk = epic, SteerWithCamera = true, DesiredYaw = 0f };

        private List<Step> Course(string which)
        {
            var list = new List<Step>();
            if (which.StartsWith("ziehfilm")) return DrawFilmCourse();
            if (which.StartsWith("angriffsfilm")) { _wechselGezogen = which.Contains("wechsel"); return AttackFilmCourse(which.Contains("einhand") || _wechselGezogen ? "b_" : null); }
            if (which.StartsWith("waffen")) return WeaponCourse();
            if (which.StartsWith("treffer")) return HitCourse();   // 22.10.: Kampfreaktionen
            if (which.StartsWith("anatomie")) return AnatomyCourse();
            if (which.StartsWith("haltung")) return PostureCourse();
            if (which.StartsWith("rucke")) return JerkCourse();
            if (which.StartsWith("clips"))
            {
                // 16.10. abends: nur die CMU-Clips Bild fuer Bild, mit Naht-Mitschrift (schnell, ~1 min)
                _clipFilter = "CMU_";
                list.Add(new Step { Id = "00_clips", Title = "CMU-Clips Bild fuer Bild", ClipAudit = true, Lane = "Flat", Command = t => Still() });
                return list;
            }
            void Add(string id, string title, string lane, float dur, Func<float, MoveCommand> cmd, Shot shot = Shot.Side, int frames = 12,
                float lead = 0f, Vector3 offset = default, float yaw = 0f, Action start = null)
                => list.Add(new Step { Id = id, Title = title, Lane = lane, Duration = dur, Command = cmd, Shot = shot, Frames = frames, Lead = lead, Offset = offset, Yaw = yaw, Start = start });
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            // 17.10. spaet (Bewegung v2, Max: Abnahme an der Bewegung statt an Standbildern): je Gangart rundum mit 16 Bildern ueber
            // etwa einen Schrittzyklus nach 1,5 s Anlauf – daraus entstehen ausserhalb von Unity animierte Bilder (GIF) je Ansicht.
            // 18.10. (Max: Fehler in Beinen und Haltung): Stufen-Pruefung – jede Gangart dreimal (nur Animator / ohne Fuss-Schicht /
            // alles an) mit Skelett-Mitschrift, damit klar wird, in welcher Stufe X-Beine, Ueberkreuzen oder Fusswinkel entstehen.
            if (which.StartsWith("stufen"))
            {
                void Stufe(bool feet, bool arms, bool dyn, bool hands, bool inert, bool all)
                {
                    Procedural.CharacterMotionPoser.DebugNoFeet = feet; Procedural.CharacterMotionPoser.DebugNoArms = arms; Procedural.CharacterMotionPoser.DebugNoDynamics = dyn;
                    Procedural.WeaponHandPoser.DebugOff = hands; Procedural.AutoInertializer.GlobalEnabled = !inert;
                    Procedural.CharacterMotionPoser.GlobalEnabled = !all;
                }
                var stufen = new (string id, bool f, bool a, bool d, bool h, bool i, bool all)[]
                {
                    ("a_animator", true, true, true, true, true, true), ("b_ohnefuss", true, false, false, false, false, false), ("c_alles", false, false, false, false, false, false),
                };
                var gaenge = new (string id, Func<float, MoveCommand> cmd)[]
                {
                    ("stehen", t => Still()), ("gehen", t => Fwd(walk: true)), ("joggen", t => Fwd(jog: true)), ("rennen", t => Fwd()), ("sprint", t => Fwd(sprint: true)),
                };
                foreach (var g in gaenge)
                    foreach (var st in stufen)
                    {
                        var c = st;
                        Add(g.id + "_" + c.id, g.id + " · " + c.id, "Flat", 1.6f, g.cmd, Shot.Side, 1, 1.5f, start: () => Stufe(c.f, c.a, c.d, c.h, c.i, c.all));
                    }
                // 18.10.: Teile der Fuss-Schicht einzeln aus (Gehen)
                void Teil(bool wa, bool rl, bool cl, bool tg, bool pe)
                {
                    Stufe(false, false, false, false, false, false);
                    Procedural.CharacterMotionPoser.DebugNoWarp = wa; Procedural.CharacterMotionPoser.DebugNoRunLock = rl; Procedural.CharacterMotionPoser.DebugNoCapLift = cl;
                    Procedural.CharacterMotionPoser.DebugNoToeGuard = tg; Procedural.CharacterMotionPoser.DebugNoPelvis = pe;
                }
                Add("gehen_d_ohnedehnung", "gehen ohne Schrittdehnung", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(true, false, false, false, false));
                Add("gehen_e_ohneverriegelung", "gehen ohne Lauf-Verriegelung", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(false, true, false, false, false));
                Add("gehen_f_ohnehubgrenze", "gehen ohne Schwungbein-Grenze", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(false, false, true, false, false));
                Add("gehen_g_ohnezehenschutz", "gehen ohne Zehenschutz", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(false, false, false, true, false));
                Add("gehen_h_ohnebecken", "gehen ohne Beckenabsenkung", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(false, false, false, false, true));
                Add("gehen_i_nichts", "gehen Fuss-Schicht ohne alle Teile", "Flat", 1.6f, t => Fwd(walk: true), Shot.Side, 1, 1.5f, start: () => Teil(true, true, true, true, true));
                Add("z_ende", "Ende", "Flat", 0.2f, t => Still(), Shot.Side, 1, 0f, start: () => { Teil(false, false, false, false, false); });
                return list;
            }
            if (which.StartsWith("kopf"))
            {
                Add("k0_ruhe", "Einschwingen (Stand)", "Flat", 2.0f, t => Still(), Shot.Side, 1, 2.0f);   // Lab 18.10.: erster Schritt zeigte Arm hoch (Einschwingen)
                Add("k1_kopf", "Kopf und Gesicht", "Flat", 0.3f, t => Still(), Shot.Head, 1, 1.5f);
                // (18.10.: SSAO-Gegenprobe zur Laufzeit entfernt – Abschalten im Play-Modus zerstörte die Darstellung)
                return list;
            }
            // 19.10. abends (LODs): jede Detailstufe des Körpers erzwungen in der Kopf-Ansicht (AppearanceRig baut die LODGroup); „stufen“ ist die Fuss-Schicht
            if (which.StartsWith("lod"))
            {
                Add("s_ruhe", "Einschwingen (Stand)", "Flat", 2.0f, t => Still(), Shot.Side, 1, 2.0f);
                for (int i = 0; i < 4; i++)
                {
                    int stufe = i;
                    Add("s" + i + "_stufe", "Detailstufe " + i, "Flat", 0.3f, t => Still(), Shot.Head, 1, 1.0f, start: () => StufeErzwingen(stufe));
                }
                Add("z_ende", "Ende", "Flat", 0.2f, t => Still(), Shot.Side, 1, 0f, start: () => StufeErzwingen(-1));
                return list;
            }
            // 20.10. (Max: „fix everything“ – Fußgleiten): Vergleich ohne/mit Lauf-Verriegelung je Gangart. Die Voreinstellung
            // (V2Fussverriegelung, Max 18.10. „UAL pur“) bleibt unverändert – der Kurs schaltet nur für seine Schritte um und zurück.
            if (which.StartsWith("gleiten"))
            {
                bool vorher = Procedural.CharacterMotionPoser.V2Fussverriegelung;
                foreach (var (kz, an) in new[] { ("a_aus", false), ("b_an", true) })
                {
                    bool modus = an;
                    Action setze = () => Procedural.CharacterMotionPoser.V2Fussverriegelung = modus;
                    Add(kz + "1_walk", "Gehen · Verriegelung " + (an ? "an" : "aus"), "Flat", 2.4f, t => Fwd(walk: true), Shot.Side, 6, 1.5f, start: setze);
                    Add(kz + "2_jog", "Joggen · Verriegelung " + (an ? "an" : "aus"), "Flat", 1.6f, t => Fwd(jog: true), Shot.Side, 6, 1.5f, start: setze);
                    Add(kz + "3_run", "Rennen · Verriegelung " + (an ? "an" : "aus"), "Flat", 1.4f, t => Fwd(), Shot.Side, 6, 1.5f, start: setze);
                    Add(kz + "4_sprint", "Sprint · Verriegelung " + (an ? "an" : "aus"), "Flat", 1.2f, t => Fwd(sprint: true), Shot.Side, 6, 1.5f, start: setze);
                    Add(kz + "5_stopp", "Anhalten aus dem Rennen · Verriegelung " + (an ? "an" : "aus"), "Flat", 1.6f, t => t < 0.6f ? Fwd() : Still(), Shot.Side, 6, 1.5f, start: setze);
                }
                Add("z_ende", "Ende", "Flat", 0.2f, t => Still(), Shot.Side, 1, 0f, start: () => Procedural.CharacterMotionPoser.V2Fussverriegelung = vorher);
                return list;
            }
            // 20.10. (REQ-20261020-010 Block A): Reaktion des Animators – sieht er den Motorzustand desselben Bildes? Je Abschnitt
            // „Animator-Verzug“ (AnimatorVerzugMessung) neben Fußgleiten/Bodendurchdringung; Loslaufen/Anhalten, alle Gangarten,
            // seitwärts, rückwärts, Sprünge.
            if (which.StartsWith("reaktion"))
            {
                Add("r01_start", "Loslaufen aus dem Stand", "Flat", 1.4f, t => t < 0.2f ? Still() : Fwd(), Shot.Side, 1, 0.5f);
                Add("r02_stopp", "Anhalten aus dem Rennen", "Flat", 1.8f, t => t < 0.15f ? Fwd() : Still(), Shot.Side, 1, 1.5f);
                Add("r03_walk", "Gehen", "Flat", 2.4f, t => Fwd(walk: true), Shot.Side, 1, 1.5f);
                Add("r04_jog", "Joggen", "Flat", 1.6f, t => Fwd(jog: true), Shot.Side, 1, 1.5f);
                Add("r05_run", "Rennen", "Flat", 1.4f, t => Fwd(), Shot.Side, 1, 1.5f);
                Add("r06_sprint", "Sprint", "Flat", 1.2f, t => Fwd(sprint: true), Shot.Side, 1, 1.5f);
                Add("r07_seit", "Seitlich laufen (rechts)", "Flat", 2.0f, t => new MoveCommand { Strafe = 1f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Front, 1, 1.0f);
                Add("r08_rueck", "Rueckwaerts laufen", "Flat", 2.0f, t => new MoveCommand { Forward = -1f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Side, 1, 1.0f);
                Add("r09_sprung", "Sprung aus dem Stand", "Flat", 1.6f, t => new MoveCommand { Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Side, 1);
                Add("r10_laufsprung", "Sprung aus dem Rennen", "Flat", 1.8f, t => new MoveCommand { Forward = 1f, Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Side, 1, 1.2f);
                return list;
            }
            // 21.10.2026 (Tripo-Kleidung, Max: „gerne“): Kleidung in Bewegung – Gangarten, Sprung, Kampf, Rolle, Sitzen (Hüfte stark
            // gebeugt) und Jubeln (Arme hoch) mit angezogenem Gambeson (Tunika) und Reisehose, dazu Nahaufnahmen (Schulter, Achsel,
            // Rücken/Kragen, Schritt, Knie, Saum) und je Detailstufe ein Rundum-Bild. Auf CHR_W_TR01 erscheint das echte Kleid, auf
            // allen anderen Körpern die Schalen wie bisher („kleidung-mann“ als Rückfall-Vergleich).
            if (which.StartsWith("kleidung"))
            {
                string[] tank = { "item.warden_blade", "item.oak_shield", null, "class.warden", "item.frayed_gambeson", "item.travel_trousers" };
                string[] jaeger = { "item.cinder_dagger", "item.cinder_dagger", null, "class.ashstalker", "item.frayed_gambeson", "item.travel_trousers" };
                Action kampf = () => _orchestrator?.ForceStance(MotionStance.Combat);
                MoveCommand Sprung(float t, bool lauf) => new MoveCommand { Forward = lauf ? 1f : 0f, Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f };
                list.Add(new Step { Id = "k00_ruhe", Title = "Stehen (rundum)", Lane = "Flat", Duration = 1.2f, Lead = 1.5f, Shot = Shot.Around, Frames = 3, Command = t => Still(), Loadout = tank });
                Add("k01_nah_ruhe", "Stehen · Nahaufnahmen", "Flat", 0.3f, t => Still(), Shot.Kleid, 1, 0.5f);
                Add("k02_gehen", "Gehen (rundum)", "Flat", 1.2f, t => Fwd(walk: true), Shot.Around, 6, 1.5f);
                Add("k03_nah_gehen", "Gehen · Nahaufnahmen", "Flat", 1.0f, t => Fwd(walk: true), Shot.Kleid, 2, 1.5f);
                Add("k04_rennen", "Rennen (rundum)", "Flat", 0.8f, t => Fwd(), Shot.Around, 6, 1.5f);
                Add("k05_nah_rennen", "Rennen · Nahaufnahmen", "Flat", 0.7f, t => Fwd(), Shot.Kleid, 2, 1.5f);
                Add("k06_sprint", "Sprint (rundum)", "Flat", 0.7f, t => Fwd(sprint: true), Shot.Around, 6, 1.5f);
                Add("k07_sprung", "Sprung aus dem Stand (rundum)", "Flat", 1.4f, t => Sprung(t, false), Shot.Around, 6);
                Add("k08_nah_sprung", "Sprung · Nahaufnahmen", "Flat", 0.9f, t => Sprung(t, false), Shot.Kleid, 3);
                Add("k09_laufsprung", "Sprung aus dem Rennen (rundum)", "Flat", 1.8f, t => Sprung(t, true), Shot.Around, 6, 1.2f);
                Add("k10_ziehen", "Waffe ziehen (rundum)", "Flat", 1.0f, t => Still(), Shot.Around, 6, 0.4f, start: kampf);
                for (int i = 0; i < 3; i++) { int k = i; list.Add(Act("k1" + (i + 1) + "_angriff" + (i + 1), "Angriff " + (i + 1), () => Attack(AttackKind.Combo, k))); }
                list.Add(Act("k14_spezial", "Spezialangriff", () => Attack(AttackKind.Special, 0)));
                { Step nah = Act("k15_nah_angriff", "Angriff · Nahaufnahmen", () => Attack(AttackKind.Combo, 0), 0.9f); nah.Shot = Shot.Kleid; nah.Frames = 2; list.Add(nah); }
                list.Add(new Step { Id = "k16_wegstecken", Title = "Wegstecken (rundum)", Lane = "Flat", Duration = 1.2f, Shot = Shot.Around, Frames = 4, Command = t => Still(),
                    PreCombat = true, Start = () => _orchestrator?.ForceStance(MotionStance.Exploration) });
                list.Add(new Step { Id = "k17_jaeger", Title = "Aschenjaeger (Rolle vorbereiten)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => Still(), Loadout = jaeger });
                list.Add(Act("k18_rolle_vor", "Ausweichrolle nach vorn", () => Dodge(Vector3.forward), 1.0f, true));
                list.Add(Act("k19_rolle_seite", "Ausweichrolle zur Seite", () => Dodge(Vector3.right), 1.0f, true));
                list.Add(new Step { Id = "k20_sitzen", Title = "Sitzen (Hüfte stark gebeugt, rundum)", Lane = "Flat", Duration = 1.0f, Lead = 2.4f, Shot = Shot.Around, Frames = 2, Command = t => Still(), Start = () => Emotes()?.Play(EmoteId.Sit) });
                list.Add(new Step { Id = "k21_nah_sitzen", Title = "Sitzen · Nahaufnahmen", Lane = "Flat", Duration = 0.3f, Lead = 2.4f, Shot = Shot.Kleid, Frames = 1, Command = t => Still(), Start = () => Emotes()?.Play(EmoteId.Sit) });
                list.Add(new Step { Id = "k22_jubeln", Title = "Jubeln (Arme hoch, rundum)", Lane = "Flat", Duration = 2.0f, Shot = Shot.Around, Frames = 4, Command = t => Still(), Start = () => Emotes()?.Play(EmoteId.Cheer) });
                list.Add(new Step { Id = "k23_nah_jubeln", Title = "Jubeln · Nahaufnahmen", Lane = "Flat", Duration = 0.3f, Lead = 0.7f, Shot = Shot.Kleid, Frames = 1, Command = t => Still(), Start = () => Emotes()?.Play(EmoteId.Cheer) });
                for (int i = 0; i < 3; i++) { int stufe = i; Add("k3" + i + "_stufe" + i, "Detailstufe " + i + " (rundum)", "Flat", 0.3f, t => Still(), Shot.Around, 1, 0.5f, start: () => StufeErzwingen(stufe)); }
                Add("z_ende", "Ende", "Flat", 0.2f, t => Still(), Shot.Side, 1, 0f, start: () => StufeErzwingen(-1));
                return list;
            }
            if (which.StartsWith("film"))
            {
                Add("f1_idle", "Stehen", "Flat", 2.0f, t => Still(), Shot.Around, 16, 0.5f);
                Add("f2_walk", "Gehen", "Flat", 1.2f, t => Fwd(walk: true), Shot.Around, 16, 1.5f);
                Add("f3_jog", "Joggen", "Flat", 0.8f, t => Fwd(jog: true), Shot.Around, 16, 1.5f);
                Add("f4_run", "Rennen", "Flat", 0.7f, t => Fwd(), Shot.Around, 16, 1.5f);
                Add("f5_sprint", "Sprint", "Flat", 0.6f, t => Fwd(sprint: true), Shot.Around, 16, 1.5f);
                // 17.10. spätabends: Laufsprung und Sprung aus dem Stand (Bewegung v4, CMU 127_25), rundum
                Add("f6_runjump", "Sprung aus dem Rennen", "Flat", 1.8f, t => new MoveCommand { Forward = 1f, Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Around, 16, 1.2f);   // wie 19_runjump
                Add("f7_jump", "Sprung aus dem Stand", "Flat", 1.6f, t => new MoveCommand { Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Around, 16);   // wie 18_jump
                return list;
            }

            Add("00_around_idle", "Stehen (rundum)", "Flat", 3f, t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, Shot.Around, 6);
            // 16.10. (Max: „sieht komplett verbuggt aus“): echte Ausruestung wie im Spiel – vorher nur der Bewegungssatz ohne
            // Waffenmodell, die Haende griffen beim Ziehen ins Leere und die Handhaltung passte zu keiner Waffe.
            { Step s0 = list[0]; s0.Loadout = new[] { "item.warden_blade", "item.oak_shield", null, "class.warden" }; list[0] = s0; }
            Add("00_around_epic", "Epischer Gang (rundum)", "Flat", 2.4f, t => Fwd(epic: true), Shot.Around, 6, 1.5f);
            Add("00_around_walk", "Gehen (rundum)", "Flat", 2.4f, t => Fwd(walk: true), Shot.Around, 6, 1.5f);
            Add("00_around_jog", "Joggen (rundum)", "Flat", 1.2f, t => Fwd(jog: true), Shot.Around, 6, 1.5f);
            Add("00_around_run", "Rennen (rundum)", "Flat", 0.9f, t => Fwd(), Shot.Around, 6, 1.5f);
            // 16.10. (Max: „guckst du auch von hinten und vorne?“): Sprint, Anhalten und Sprung ebenfalls rundum – die
            // Seitenansicht versteckte Schild am Unterarm und pendelndes Schwert.
            Add("00_around_sprint", "Sprint (rundum)", "Flat", 0.8f, t => Fwd(sprint: true), Shot.Around, 6, 1.5f);
            Add("00_around_stop", "Anhalten (rundum)", "Flat", 1.4f, t => t < 0.15f ? Fwd() : new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, Shot.Around, 6, 1.5f);
            Add("00_around_jump", "Sprung (rundum)", "Flat", 1.4f, t => new MoveCommand { Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Around, 6);
            Add("01_idle", "Stehen", "Flat", 3f, t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f });
            Add("02_walk", "Gehen", "Flat", 4f, t => Fwd(walk: true), lead: 1.5f);
            Add("02b_epic", "Epischer Gang", "Flat", 4f, t => Fwd(epic: true), lead: 1.5f);
            Add("02c_jog", "Joggen", "Flat", 4f, t => Fwd(jog: true), lead: 1.5f);
            Add("03_run", "Rennen (Standard)", "Flat", 4f, t => Fwd(), lead: 1.5f);
            Add("04_sprint", "Sprint", "Flat", 4f, t => Fwd(sprint: true), lead: 1.5f);
            Add("05_start", "Anfahren aus dem Stand", "Flat", 1.4f, t => t < 0.2f ? new MoveCommand { SteerWithCamera = true } : Fwd());
            Add("06_stop", "Anhalten aus dem Rennen", "Flat", 1.8f, t => t < 0.15f ? Fwd() : new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, lead: 1.5f);
            Add("07_turn90", "Drehen im Stand 90° (Maus)", "Flat", 1.6f, t => new MoveCommand { SteerWithCamera = true, DesiredYaw = t > 0.15f ? 90f : 0f }, Shot.ThreeQuarter);
            Add("08_turn180", "Drehen im Stand 180° (Maus)", "Flat", 1.6f, t => new MoveCommand { SteerWithCamera = true, DesiredYaw = t > 0.15f ? 180f : 0f }, Shot.ThreeQuarter);
            Add("09_turnkeys", "Drehen im Stand mit Tasten", "Flat", 2f, t => new MoveCommand { Turn = 1f }, Shot.ThreeQuarter);
            Add("10_cut90", "Rennen mit 90°-Richtungswechsel", "Flat", 2.2f,
                t => new MoveCommand { Forward = 1f, SteerWithCamera = true, DesiredYaw = t > 0.3f ? 90f : 0f }, Shot.Top, 12, 1.0f);
            Add("11_strafe", "Seitlich laufen (rechts)", "Flat", 3f, t => new MoveCommand { Strafe = 1f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Front, 12, 1.0f);
            Add("12_back", "Rueckwaerts laufen", "Flat", 3f, t => new MoveCommand { Forward = -1f, SteerWithCamera = true, DesiredYaw = 0f }, Shot.Side, 12, 1.0f);
            Add("13_slope20_up", "Hang 20° hinauf", "Slope20", 3.5f, t => Fwd(), lead: 0.2f);
            Add("14_slope20_down", "Hang 20° hinab", "Slope20Top", 3.5f, t => Fwd(), lead: 0.2f);
            Add("15_stairs_up", "Treppe hinauf (fein)", "Stairs", 3.2f, t => Fwd(walk: true), lead: 0.2f);
            Add("16_stairs_coarse", "Grobe Stufen hinauf", "StairsCoarse", 3f, t => Fwd(walk: true), lead: 0.2f);
            Add("17_obstacles", "Kleine Hindernisse (Steine, Ast)", "Obstacles", 4f, t => Fwd(), lead: 0.2f);
            Add("20_draw", "Schwert ziehen (Kampf beginnt)", "Flat", 1.0f, t => Still(), Shot.Around, 6, 0.4f,
                start: () => _orchestrator?.ForceStance(MotionStance.Combat));
            Add("21_combat_walk", "Im Kampf gehen (Waffe in der Hand)", "Flat", 1.3f, t => Fwd(walk: true), Shot.Around, 6, 1.0f,
                start: () => _orchestrator?.ForceStance(MotionStance.Combat));
            Add("22_sheathe", "Schwert wegstecken", "Flat", 1.2f, t => Still(), Shot.Around, 6, 0.3f,
                start: () => _orchestrator?.ForceStance(MotionStance.Exploration));
            Add("18_jump", "Sprung aus dem Stand", "Flat", 1.6f, t => new MoveCommand { Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f });
            Add("19_runjump", "Sprung aus dem Rennen", "Flat", 1.8f, t => new MoveCommand { Forward = 1f, Jump = t > 0.1f && t < 0.15f, SteerWithCamera = true, DesiredYaw = 0f }, lead: 1.2f);
            return list;
        }

        /// <summary>
        /// Fehlersuche 16.10. abends (Max: „sieht immer noch schlecht aus“, Anatomie-Pruefung: Becken/Kopf/Fuss-Rucke bis Stufe 75):
        /// Joggen und Sprint je mit einer abgeschalteten Schicht, mit Ruck-Messung – welche Schicht erzeugt die Rucke?
        /// Keine Bildaufnahmen waehrend der Messung (Aufnahme-Haenger wuerden mitgemessen).
        /// </summary>
        private List<Step> JerkCourse()
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            _clipFilter = "CMU_"; // nur die Laufclips Bild fuer Bild (die Quaternius-Clips sind hier nicht die Frage)
            list.Add(new Step { Id = "00_clips", Title = "CMU-Clips Bild fuer Bild", ClipAudit = true, Lane = "Flat", Command = t => Still() });
            void Flags(bool feet, bool arms, bool dyn, bool hands, bool inert, bool all)
            {
                Procedural.CharacterMotionPoser.DebugNoFeet = feet; Procedural.CharacterMotionPoser.DebugNoArms = arms; Procedural.CharacterMotionPoser.DebugNoDynamics = dyn;
                Procedural.WeaponHandPoser.DebugOff = hands;
                Procedural.AutoInertializer.GlobalEnabled = !inert;
                Procedural.CharacterMotionPoser.GlobalEnabled = !all;
                _orchestrator?.ForceStance(MotionStance.Exploration);
            }
            var varianten = new (string id, string titel, bool f, bool a, bool d, bool h, bool i, bool all)[]
            {
                ("r0_alles", "alles an", false, false, false, false, false, false),
                ("r1_ohne_fuesse", "ohne Fuss-Schicht", true, false, false, false, false, false),
                ("r2_ohne_dynamik", "ohne Rumpf-Dynamik", false, false, true, false, false, false),
                ("r3_ohne_handposer", "ohne Handhaltung", false, false, false, true, false, false),
                ("r4_ohne_traegheit", "ohne Traegheitsglaettung", false, false, false, false, true, false),
                ("r5_nur_animator", "nur Animator (alles aus)", true, true, true, true, true, true),
            };
            foreach (var v in varianten)
            {
                var c = v;
                list.Add(new Step { Id = c.id + "_jog", Title = "Joggen · " + c.titel, Lane = "Flat", Duration = 3f, Lead = 1.5f, Shot = Shot.Side, Frames = 1, Command = t => Fwd(jog: true), Start = () => Flags(c.f, c.a, c.d, c.h, c.i, c.all) });
                list.Add(new Step { Id = c.id + "_sprint", Title = "Sprint · " + c.titel, Lane = "Flat", Duration = 3f, Lead = 1.5f, Shot = Shot.Side, Frames = 1, Command = t => Fwd(sprint: true), Start = () => Flags(c.f, c.a, c.d, c.h, c.i, c.all) });
            }
            // 16.10. abends: Die CMU-Clips sind nahtlos (Clip-Mitschrift), trotzdem rucken Unterarme im reinen Animator genau alle
            // 0,68 s. Zwei Gegenproben: Gehen (anderer Clip) und nur die Basis-Schicht (Waffen-/Oberkoerper-Schichten auf 0).
            list.Add(new Step { Id = "r6_nur_animator_walk", Title = "Gehen · nur Animator (alles aus)", Lane = "Flat", Duration = 3f, Lead = 1.5f, Shot = Shot.Side, Frames = 1, Command = t => Fwd(walk: true), Start = () => Flags(true, true, true, true, true, true) });
            list.Add(new Step { Id = "r7_nur_basis_jog", Title = "Joggen · nur Animator, nur Basis-Schicht", Lane = "Flat", Duration = 3f, Lead = 1.5f, Shot = Shot.Side, Frames = 1, Command = t => Fwd(jog: true), Start = () => { Flags(true, true, true, true, true, true); SchichtenNurBasis(true); } });
            list.Add(new Step { Id = "r8_nur_basis_sprint", Title = "Sprint · nur Animator, nur Basis-Schicht", Lane = "Flat", Duration = 3f, Lead = 1.5f, Shot = Shot.Side, Frames = 1, Command = t => Fwd(sprint: true), Start = () => { Flags(true, true, true, true, true, true); SchichtenNurBasis(true); } });
            { Step s0 = list[0]; s0.Loadout = new[] { "item.warden_blade", "item.oak_shield", null, "class.warden" }; list[0] = s0; }
            list.Add(new Step { Id = "r9_ende", Title = "Ende", Lane = "Flat", Duration = 0.3f, Shot = Shot.Side, Frames = 1, Command = t => Still(), Start = () => { Flags(false, false, false, false, false, false); SchichtenNurBasis(false); } });
            return list;
        }

        private float[] _schichtGewichte;
        /// <summary>Ruck-Parcours: alle Animator-Schichten ausser der Basis auf 0 (an) bzw. zurueck (aus).</summary>
        private void SchichtenNurBasis(bool an)
        {
            Animator a = ActiveAnimator();
            if (a == null) return;
            if (an)
            {
                _schichtGewichte = new float[a.layerCount];
                for (int i = 1; i < a.layerCount; i++) { _schichtGewichte[i] = a.GetLayerWeight(i); a.SetLayerWeight(i, 0f); }
            }
            else if (_schichtGewichte != null)
            {
                for (int i = 1; i < a.layerCount && i < _schichtGewichte.Length; i++) a.SetLayerWeight(i, _schichtGewichte[i]);
                _schichtGewichte = null;
            }
        }

        /// <summary>Fehlersuche 16.10.: Stand und Gehen je mit einem abgeschalteten Teil der prozeduralen Schicht
        /// (Fuesse, Arme, Rumpf-Dynamik), um zu sehen, welcher Teil die Figur krumm macht.</summary>
        private List<Step> PostureCourse()
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            void Flags(bool feet, bool arms, bool dyn, bool hands = false, int handPart = 0)
            {
                Procedural.CharacterMotionPoser.DebugNoFeet = feet; Procedural.CharacterMotionPoser.DebugNoArms = arms; Procedural.CharacterMotionPoser.DebugNoDynamics = dyn;
                Procedural.WeaponHandPoser.DebugOff = hands;
                Procedural.WeaponHandPoser.DebugNoCarry = handPart == 1 || handPart == 4; Procedural.WeaponHandPoser.DebugNoClamp = handPart == 2 || handPart == 4; Procedural.WeaponHandPoser.DebugNoShape = handPart == 3 || handPart == 4;
                Procedural.WeaponHandPoser.DebugEarlyReturn = handPart == 5;
                _orchestrator?.ForceStance(MotionStance.Exploration);
            }
            var varianten = new (string id, string titel, bool f, bool a, bool d, bool h, int hp)[]
            {
                ("h0_alles", "alles an", false, false, false, false, 0),
                ("h7_nur_handhaltung", "nur Handhaltung (Waffe)", true, true, true, false, 0),
                ("h8_gar_nichts", "gar nichts", true, true, true, true, 0),
                ("h12_ohne_drei", "Handhaltung ohne Tragen+Gelenke+Finger", true, true, true, false, 4),
                ("h13_frueh_raus", "Handhaltung nur Tragen (dann raus)", true, true, true, false, 5),
            };
            foreach (var v in varianten)
            {
                var c = v;
                list.Add(new Step { Id = c.id + "_stehen", Title = "Stehen · " + c.titel, Lane = "Flat", Duration = 2f, Lead = 1.5f, Shot = Shot.Side, Frames = 2, Command = t => Still(), Start = () => Flags(c.f, c.a, c.d, c.h, c.hp) });
                list.Add(new Step { Id = c.id + "_gehen", Title = "Gehen · " + c.titel, Lane = "Flat", Duration = 1.6f, Lead = 1.5f, Shot = Shot.Side, Frames = 4, Command = t => Fwd(walk: true), Start = () => Flags(c.f, c.a, c.d, c.h, c.hp) });
            }
            { Step s0 = list[0]; s0.Loadout = new[] { "item.warden_blade", "item.oak_shield", null, "class.warden" }; list[0] = s0; }
            list.Add(new Step { Id = "h9_ende", Title = "Ende", Lane = "Flat", Duration = 0.3f, Shot = Shot.Side, Frames = 1, Command = t => Still(), Start = () => Flags(false, false, false, false) });
            return list;
        }

        /// <summary>21.10. abends (Max: „jedes fps der Ausrüsten-/Wegstecken-Animation anschauen und korrigieren, bis es cool
        /// aussieht“): je Ausrüstung Ruhe, Ziehen und Wegstecken als dichter Film – 16 Bilder rundum (vorn, seitlich, hinten,
        /// schräg). Gleiche Ausrüstung und Messwerte wie der Waffen-Kurs.</summary>
        private List<Step> DrawFilmCourse()
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            foreach (string[] lo in Loadouts)
            {
                string id = lo[0], title = lo[1];
                if (id.Contains("waffenlos")) continue;
                string[] kit = KitOf(lo);
                list.Add(new Step { Id = id + "_1_ruhe", Title = title + " · Stehen", Lane = "Flat", Duration = 0.5f, Lead = 0.4f, Shot = Shot.Around, Frames = 1, Command = t => Still(), Loadout = kit });
                list.Add(new Step { Id = id + "_1b_gehen", Title = title + " · Gehen (gesteckt)", Lane = "Flat", Duration = 1.2f, Lead = 0.8f, Shot = Shot.Around, Frames = 3, Command = t => Fwd(walk: true) });
                list.Add(new Step { Id = id + "_1c_laufen", Title = title + " · Laufen (gesteckt)", Lane = "Flat", Duration = 1.0f, Lead = 0.8f, Shot = Shot.Around, Frames = 3, Command = t => Fwd() });
                list.Add(new Step { Id = id + "_2_ziehen", Title = title + " · Ziehen (Film)", Lane = "Flat", Duration = 1.3f, Lead = 0f, Shot = Shot.Around, Frames = 16, Command = t => Still(),
                    Start = () => _orchestrator?.ForceStance(MotionStance.Combat) });
                list.Add(new Step { Id = id + "_3_wegstecken", Title = title + " · Wegstecken (Film)", Lane = "Flat", Duration = 1.3f, Lead = 0f, Shot = Shot.Around, Frames = 16, Command = t => Still(),
                    PreCombat = true, Start = () => _orchestrator?.ForceStance(MotionStance.Exploration) });
            }
            return list;
        }

        /// <summary>
        /// 21.10. nachts (Max: Angriffe „unsauber“, „Geschwindigkeit sieht nicht richtig aus“): jeder Angriff jeder Ausruestung als
        /// Film – 16 Bilder rundum ueber 0,9 s (Hieb 0,5 s + Ausklang), dazu die Anatomie-/Durchdringungspruefung.
        /// </summary>
        private bool _wechselGezogen;   // „angriffsfilm-wechsel“: Ausrüstung mit gezogener Waffe wechseln (Prüfung des Wechsels)
        private List<Step> AttackFilmCourse(string only)
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            foreach (string[] lo in Loadouts)
            {
                string id = lo[0], title = lo[1];
                if (only != null && !id.StartsWith(only) && !id.StartsWith("a_") && !id.StartsWith("c_")) continue;   // „angriffsfilm-einhand“: Schwert+Schild, dann Einhandschwert (Wechsel bei gezogener Waffe)
                string[] kit = KitOf(lo);
                // erst wegstecken, dann wechseln (Wechsel mit gezogener Waffe ist ein eigener offener Punkt, REP-067)
                if (!_wechselGezogen) list.Add(new Step { Id = id + "_00_weg", Title = title + " · Wegstecken vor dem Wechsel", Lane = "Flat", Duration = 0.2f, Lead = 1.8f, Shot = Shot.Around, Frames = 1, Command = t => Still(),
                    Start = () => _orchestrator?.ForceStance(MotionStance.Exploration) });
                list.Add(new Step { Id = id + "_0_kampf", Title = title + " · Kampfhaltung", Lane = "Flat", Duration = 0.4f, Lead = 2.6f, Shot = Shot.Around, Frames = 1, Command = t => Still(), Loadout = kit,
                    Start = () => _orchestrator?.ForceStance(MotionStance.Combat) });
                if (IsBow(lo))
                {
                    Step s1 = Act(id + "_1_schuss", title + " · Schuss", () => Attack(AttackKind.Combo, 0), 2.4f); s1.Frames = 16; list.Add(s1);
                    Step s2 = Act(id + "_2_zielschuss", title + " · Gezielter Schuss", () => Attack(AttackKind.Special, 0), 2.8f); s2.Frames = 16; list.Add(s2);
                    continue;
                }
                for (int i = 0; i < 3; i++)
                {
                    int k = i;
                    Step s = Act(id + "_" + (i + 1) + "_angriff", title + " · Angriff " + (i + 1), () => Attack(AttackKind.Combo, k), 0.9f);
                    s.Frames = 16; list.Add(s);
                }
                Step sp = Act(id + "_4_spezial", title + " · Spezialangriff", () => Attack(AttackKind.Special, 0), 1.2f);
                sp.Frames = 16; list.Add(sp);
            }
            return list;
        }

        private List<Step> WeaponCourse()
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            foreach (string[] lo in Loadouts)
            {
                string id = lo[0], title = lo[1];
                string[] kit = KitOf(lo);
                Action combat = () => _orchestrator?.ForceStance(MotionStance.Combat);
                list.Add(new Step { Id = id + "_1_ruhe", Title = title + " · Stehen", Lane = "Flat", Duration = 0.9f, Lead = 0.4f, Shot = Shot.Around, Frames = 3, Command = t => Still(), Loadout = kit });
                list.Add(new Step { Id = id + "_2a_ziehen", Title = title + " · Ziehen", Lane = "Flat", Duration = 1.15f, Lead = 0f, Shot = Shot.Around, Frames = 6, Command = t => Still(), Start = combat });
                list.Add(new Step { Id = id + "_2_kampf", Title = title + " · Kampfhaltung", Lane = "Flat", Duration = 0.9f, Lead = 1.3f, Shot = Shot.Around, Frames = 3, Command = t => Still(), Start = combat });
                list.Add(new Step { Id = id + "_3_kampfgehen", Title = title + " · Im Kampf gehen", Lane = "Flat", Duration = 1.2f, Lead = 1.1f, Shot = Shot.Around, Frames = 4, Command = t => Fwd(walk: true), Start = combat });
                list.Add(new Step { Id = id + "_5_wegstecken", Title = title + " · Wegstecken", Lane = "Flat", Duration = 1.2f, Lead = 0f, Shot = Shot.Around, Frames = 6, Command = t => Still(),
                    PreCombat = true, Start = () => _orchestrator?.ForceStance(MotionStance.Exploration) });
                list.Add(new Step { Id = id + "_4_haende", Title = title + " · Haende (Kampf)", Lane = "Flat", Duration = 0.2f, Lead = 1.3f, Shot = Shot.Hands, Frames = 1, Command = t => Still(), Start = combat });
            }
            return list;
        }

        /// <summary>
        /// Anatomie-Pruefung: jeder Clip Bild fuer Bild, dann alles, was die Spielfigur tut – Stehen (lange, damit die
        /// Standhaltung greift), Fortbewegung, Springen, je Ausruestung Ziehen/Kampf/Angriffe/Wegstecken, Ausweichen,
        /// Treffer, Emotes, Tod. Gemessen wird die Endpose nach allen Schichten (<see cref="AnatomyAuditor"/>).
        /// </summary>
        private List<Step> AnatomyCourse()
        {
            var list = new List<Step>();
            MoveCommand Still() => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
            string[] none = { null, null };
            list.Add(new Step { Id = "00_clips", Title = "Alle Animationsclips Bild fuer Bild", ClipAudit = true, Lane = "Flat", Command = t => Still() });
            list.Add(new Step { Id = "01_ruhe_lang", Title = "Stehen ohne Waffe (Standhaltung)", Lane = "Flat", Duration = 4f, Lead = 2.5f, Shot = Shot.Around, Frames = 4, Command = t => Still(), Loadout = none });
            foreach (Step s0 in Course(""))
            {
                if (s0.Id.StartsWith("00_") || s0.Id.StartsWith("20_") || s0.Id.StartsWith("21_") || s0.Id.StartsWith("22_")) continue;
                Step s = s0;
                s.Frames = Mathf.Min(s.Frames, 6);
                list.Add(s);
            }
            foreach (string[] lo in Loadouts)
            {
                string id = "L" + lo[0], title = lo[1];
                string[] kit = KitOf(lo);
                bool ranged = IsBow(lo);
                Action combat = () => _orchestrator?.ForceStance(MotionStance.Combat);
                list.Add(new Step { Id = id + "_1_ruhe", Title = title + " · Stehen", Lane = "Flat", Duration = 3f, Lead = 2.5f, Shot = Shot.Around, Frames = 3, Command = t => Still(), Loadout = kit });
                list.Add(new Step { Id = id + "_2_ziehen", Title = title + " · Ziehen", Lane = "Flat", Duration = 1.3f, Shot = Shot.Around, Frames = 4, Command = t => Still(), Start = combat });
                list.Add(new Step { Id = id + "_3_kampfgehen", Title = title + " · Im Kampf gehen", Lane = "Flat", Duration = 1.4f, Lead = 1.1f, Shot = Shot.Around, Frames = 3, Command = t => Fwd(walk: true), Start = combat });
                if (ranged)
                {
                    list.Add(Act(id + "_4a_schuss", title + " · Automatischer Schuss (Pfeil holen, spannen, loesen)", () => Attack(AttackKind.Combo, 0), 3.4f));
                    list.Add(Act(id + "_4b_zielschuss", title + " · Gezielter Schuss", () => Attack(AttackKind.Special, 0), 3.2f));
                }
                else
                {
                    for (int i = 0; i < 3; i++) { int k = i; list.Add(Act(id + "_4" + (char)('a' + i) + "_angriff" + (i + 1), title + " · Angriff " + (i + 1), () => Attack(AttackKind.Combo, k))); }
                    list.Add(Act(id + "_4d_spezial", title + " · Spezialangriff", () => Attack(AttackKind.Special, 0)));
                }
                list.Add(new Step { Id = id + "_5_wegstecken", Title = title + " · Wegstecken", Lane = "Flat", Duration = 1.4f, Shot = Shot.Around, Frames = 4, Command = t => Still(),
                    PreCombat = true, Start = () => _orchestrator?.ForceStance(MotionStance.Exploration) });
            }
            // Ausweichen (Max 30.09.): Rolle ist die Faehigkeit des Aschenjaegers; passives Ausweichen und Schildblock fuer alle
            string[] stalker = { "item.cinder_dagger", "item.cinder_dagger", null, "class.ashstalker" };
            string[] tank = { "item.warden_blade", "item.oak_shield", null, "class.warden" };
            list.Add(new Step { Id = "90_ausweichen_vorb", Title = "Aschenjaeger (Rolle vorbereiten)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => Still(), Loadout = stalker });
            list.Add(Act("90a_rolle_vor", "Ausweichrolle nach vorn", () => Dodge(Vector3.forward), 1.0f, true));
            list.Add(Act("90b_rolle_seite", "Ausweichrolle zur Seite", () => Dodge(Vector3.right), 1.0f, true));
            list.Add(Act("90c_ausweichen_kampf", "Ausweichen im Kampf (passiv)", () => Evade(), 0.8f));
            list.Add(new Step { Id = "90d_schild_vorb", Title = "Eidwaechter (Schild)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => Still(), Loadout = tank });
            list.Add(Act("90e_schildblock", "Schildblock", () => Block(), 0.8f));
            list.Add(Act("91_treffer", "Treffer einstecken", () => Trigger("Hit"), 1.0f, true));
            TrefferSchritte(list, stalker);
            foreach (EmoteInfo e in EmoteCatalog.All)
            {
                EmoteId eid = e.Id;
                list.Add(new Step { Id = "92_emote_" + e.Command, Title = "Emote " + e.Name, Lane = "Flat", Duration = Mathf.Clamp(e.Duration, 2f, 3.2f), Shot = Shot.Around, Frames = 4, Command = t => Still(),
                    Start = () => Emotes()?.Play(eid) });
            }
            list.Add(Act("99_tod", "Tod", () => SetDead(true), 2.6f, true));
            return list;
        }

        private Step Act(string id, string title, Action start, float duration = 1.4f, bool noCombat = false) => new Step
        {
            Id = id, Title = title, Lane = "Flat", Duration = duration, Shot = Shot.Around, Frames = 4,
            Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, PreCombat = !noCombat,
            Start = noCombat ? start : () => { _orchestrator?.ForceStance(MotionStance.Combat); start(); }
        };

        private StancePoser _stancePoser => puppet != null ? puppet.GetComponentInChildren<StancePoser>() : null;
        private WeaponUser WeaponUserOf() => puppet != null ? puppet.GetComponentInChildren<WeaponUser>() : null;
        private EmotePoser Emotes() => _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<EmotePoser>() : null;

        private void Attack(AttackKind kind, int index)
        {
            WeaponUser w = WeaponUserOf();
            if (w != null && w.ActiveWeapon != null) { w.ProxyAttack(kind, index); return; }
            Animator a = ActiveAnimator();
            if (a == null) return;
            a.SetInteger("AttackIndex", index);
            Trigger(kind == AttackKind.Special ? "Special" : "Attack");
        }

        private void Reload()
        {
            WeaponUser w = WeaponUserOf();
            if (w != null && w.ActiveWeapon != null) w.ProxyReload();
            else Trigger("Reload");
        }

        private Procedural.CombatChoreographer Choreo() =>
            _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Procedural.CombatChoreographer>() : null;

        /// <summary>Ausweichrolle in Modellrichtung (lokal) – wie ein fremder Spieler, der rollt.</summary>
        private void Dodge(Vector3 localDir)
        {
            var motor = puppet != null ? puppet.GetComponentInChildren<CharacterMotor>() : null;
            Vector3 d = puppet != null ? puppet.transform.TransformDirection(localDir) : localDir;
            if (motor != null) motor.RaiseProxyDodged(d);
        }

        private void Evade() => Choreo()?.PlayEvade(puppet != null ? puppet.transform.forward : Vector3.forward);

        /// <summary>
        /// 22.10.2026 (Max: WoW-vergleichbare Trefferreaktionen; Plan REQ-20261022-CODEX-KAMPFREAKTIONEN-DETAILPLAN): gerichtete
        /// Ganzkörperreaktion der Choreografie – Zeitreihe statt Einzelbild (kurzer Impuls, Last, Rückkehr). Erwartet den Tank (Schild) als
        /// aktuelle Ausrüstung; wechselt danach zu Doppeldolchen und Stab.
        /// </summary>
        private void TrefferSchritte(List<Step> list, string[] stalker)
        {
            list.Add(Reakt("91a_treffer_vorn", "Treffer von vorn (leicht)", Vector3.forward, false));
            list.Add(Reakt("91b_treffer_hinten", "Treffer von hinten (leicht)", Vector3.back, false));
            list.Add(Reakt("91c_treffer_links", "Treffer von links (leicht)", Vector3.left, false));
            list.Add(Reakt("91d_treffer_rechts", "Treffer von rechts (leicht)", Vector3.right, false));
            list.Add(Reakt("91e_treffer_schwer", "Schwerer Treffer von vorn", Vector3.forward, true));
            list.Add(Reakt("91f_treffer_schwer_v2", "Schwerer Treffer von vorn, Variante 2", Vector3.forward, true, 1));
            list.Add(Act("91g_teilblock", "Teilblock (Schild + Restschaden)", () => { Block(); Choreo()?.PlayHit(PuppetDir(Vector3.forward), false, 0, 0.5f); }, 0.8f));
            list.Add(Treffer_im_Hieb("91h_treffer_im_hieb", "Treffer mitten im eigenen Hieb", false));
            list.Add(new Step { Id = "91i_dolche_vorb", Title = "Aschenjaeger (Doppeldolche)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, Loadout = stalker });
            list.Add(Reakt("91j_dolche_schwer", "Doppeldolche: schwerer Treffer von links", Vector3.left, true));
            list.Add(new Step { Id = "91k_stab_vorb", Title = "Glutweber (Stab)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f },
                Loadout = new[] { "item.ember_staff", null, null, "class.emberweaver" } });
            list.Add(Reakt("91l_stab_schwer", "Stab: schwerer Treffer von vorn", Vector3.forward, true));
            // Zweihänder: beide Hände bleiben am Griff; waffenlos: Hände ohne Phantom-Griff
            list.Add(new Step { Id = "91m_zweihand_vorb", Title = "Zweihandschwert", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f },
                Loadout = new[] { "item.bellringer", null, null, "class.warden" } });
            list.Add(Reakt("91n_zweihand_schwer", "Zweihänder: schwerer Treffer von vorn", Vector3.forward, true));
            list.Add(Reakt("91o_zweihand_rechts", "Zweihänder: Treffer von rechts", Vector3.right, false));
            list.Add(new Step { Id = "91p_waffenlos_vorb", Title = "Waffenlos", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f },
                Loadout = new string[] { null, null, null, "class.warden" } });
            list.Add(Reakt("91q_waffenlos_hinten", "Waffenlos: Treffer von hinten", Vector3.back, false));
            // 23.10. (Max: vorhandene Magier-Clips): Wirken mit Stab – ausgelöst und abgebrochen (Spell_Simple Enter/Loop/Shoot/Exit)
            list.Add(new Step { Id = "91r_stab_vorb", Title = "Glutweber (Stab) – Wirken", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f },
                Loadout = new[] { "item.ember_staff", null, null, "class.emberweaver" } });
            list.Add(Wirken("91s_wirken", "Zauber wirken (1,2 s) und auslösen", 1.2f, true));
            list.Add(Wirken("91t_wirken_abbruch", "Zauber wirken, nach 0,7 s abgebrochen", 0.7f, false));
        }

        /// <summary>22.10.: nur die Kampfreaktionen (Aufnahme „treffer“ / „treffer-frau“) – Schildblock, Ausweichen, Treffer.</summary>
        private List<Step> HitCourse()
        {
            var list = new List<Step>();
            string[] stalker = { "item.cinder_dagger", "item.cinder_dagger", null, "class.ashstalker" };
            string[] tank = { "item.warden_blade", "item.oak_shield", null, "class.warden" };
            list.Add(new Step { Id = "90d_schild_vorb", Title = "Eidwaechter (Schild)", Lane = "Flat", Duration = 1.2f, Lead = 0.6f, Shot = Shot.Around, Frames = 1, Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, Loadout = tank });
            list.Add(Act("90c_ausweichen_kampf", "Ausweichen im Kampf (passiv)", () => Evade(), 0.8f));
            list.Add(Act("90e_schildblock", "Schildblock", () => Block(), 0.8f));
            TrefferSchritte(list, stalker);
            return list;
        }

        /// <summary>Richtung im Figurenraum → Welt (für „Angreifer steht vorn/links …“).</summary>
        private Vector3 PuppetDir(Vector3 local) => puppet != null ? puppet.transform.TransformDirection(local) : local;

        /// <summary>22.10.: Trefferreaktion als Zeitreihe (schräg, 8 Bilder über 0,8 s) – Angreifer steht in <paramref name="von"/>.</summary>
        private Step Reakt(string id, string title, Vector3 von, bool schwer, int variante = 0) => new Step
        {
            Id = id, Title = title, Lane = "Flat", Duration = 0.8f, Shot = Shot.ThreeQuarter, Frames = 8,
            Command = t => new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f }, PreCombat = true,
            Start = () => { _orchestrator?.ForceStance(MotionStance.Combat); Choreo()?.PlayHit(PuppetDir(von), schwer, variante); }
        };

        /// <summary>23.10.: Wirken über die Animator-Brücke (wie die eigene Figur beim Wirkzeit-Zauber) – Zeitreihe über Ansetzen, Halten, Ende.</summary>
        private Step Wirken(string id, string title, float dauer, bool ausloesen)
        {
            bool ende = false;
            return new Step
            {
                Id = id, Title = title, Lane = "Flat", Duration = dauer + 1.0f, Shot = Shot.ThreeQuarter, Frames = 10, PreCombat = true,
                Start = () => { ende = false; _orchestrator?.ForceStance(MotionStance.Combat); Bridge()?.SetCasting(true, false); },
                Command = t =>
                {
                    if (!ende && t >= dauer) { ende = true; Bridge()?.SetCasting(false, ausloesen); }
                    return new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
                }
            };
        }

        private CharacterAnimatorBridge Bridge() => puppet != null ? puppet.GetComponentInChildren<CharacterAnimatorBridge>() : null;

        /// <summary>22.10.: eigener Hieb läuft, nach 0,3 s trifft ein Schlag von vorn – Reaktion setzt auf den Hieb auf, Griff bleibt.</summary>
        private Step Treffer_im_Hieb(string id, string title, bool schwer)
        {
            bool getroffen = false;
            return new Step
            {
                Id = id, Title = title, Lane = "Flat", Duration = 1.2f, Shot = Shot.ThreeQuarter, Frames = 10, PreCombat = true,
                Start = () => { getroffen = false; _orchestrator?.ForceStance(MotionStance.Combat); Attack(AttackKind.Combo, 0); },
                Command = t =>
                {
                    if (!getroffen && t >= 0.3f) { getroffen = true; Choreo()?.PlayHit(PuppetDir(Vector3.forward), schwer); }
                    return new MoveCommand { SteerWithCamera = true, DesiredYaw = 0f };
                }
            };
        }
        private void Block() => Choreo()?.PlayBlock();

        private void Trigger(string name)
        {
            Animator a = ActiveAnimator();
            if (a == null) return;
            foreach (AnimatorControllerParameter p in a.parameters) if (p.name == name) { a.SetTrigger(name); return; }
        }

        private void SetDead(bool dead)
        {
            Animator a = ActiveAnimator();
            if (a == null) return;
            foreach (AnimatorControllerParameter p in a.parameters) if (p.name == "Dead") { a.SetBool("Dead", dead); return; }
        }

        private void Equip(string[] kit)
        {
            var eq = puppet.GetComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            if (eq == null) { Debug.LogWarning("[Ashveil] Motion-Lab: Figur ohne CharacterEquipment – Lab neu bauen."); return; }
            var items = new ItemStack[Ashveil.Gameplay.Items.InventoryModel.EquipCount];
            if (!string.IsNullOrEmpty(kit[0])) items[(int)EquipmentSlot.MainHand] = new ItemStack(kit[0], 1);
            if (kit.Length > 1 && !string.IsNullOrEmpty(kit[1])) items[(int)EquipmentSlot.OffHand] = new ItemStack(kit[1], 1);
            if (kit.Length > 2 && !string.IsNullOrEmpty(kit[2])) items[(int)EquipmentSlot.Ranged] = new ItemStack(kit[2], 1);
            // 21.10.2026 (Tripo-Kleidung): Brust und Beine (kit[4]/kit[5]) – Kleidung in Bewegung (Aufnahme „kleidung-…“)
            if (kit.Length > 4 && !string.IsNullOrEmpty(kit[4])) items[(int)EquipmentSlot.Chest] = new ItemStack(kit[4], 1);
            if (kit.Length > 5 && !string.IsNullOrEmpty(kit[5])) items[(int)EquipmentSlot.Legs] = new ItemStack(kit[5], 1);
            CharacterClassDefinition cls = kit.Length > 3 && !string.IsNullOrEmpty(kit[3]) ? GameData.Class(kit[3]) : null;
            eq.Apply(items, cls != null ? cls : GameDataBootstrap.DefaultClass, 10, true);
            // Bogen-Ausruestung: Fernkampf aktiv (wie Taste F)
            WeaponUser wu = WeaponUserOf();
            if (wu != null) wu.ProxySetWeapon(!(kit.Length > 2 && !string.IsNullOrEmpty(kit[2])), true);
        }

        // ================================================================== Aufnahme

        private IEnumerator Record(string which)
        {
            _recording = true;
            _panel = false;
            Directory.CreateDirectory(OutputDir);
            if (player != null) player.enabled = false;
            yield return new WaitForSeconds(1f); // Szene/Animator einschwingen lassen

            string variant = which.Contains("frau") ? "frau" : "mann";
            bool raw = which.Contains("roh");
            // 22.10.2026 (Codex Zeitbasis): feste Spielzeit je Bild für den Vergleich 30/60/144 fps (Aufnahme „angriffsfilm-fps30“ …)
            int fps = which.Contains("fps30") ? 30 : which.Contains("fps60") ? 60 : which.Contains("fps144") ? 144 : 0;
            Time.captureDeltaTime = fps > 0 ? 1f / fps : 0f;
            Procedural.CharacterMotionPoser.GlobalEnabled = !raw;
            if (_bodies != null) _bodies.Apply(variant == "frau" ? 1 : 0);
            bool weapons = which.StartsWith("waffen") || which.StartsWith("anatomie") || which.StartsWith("ziehfilm") || which.StartsWith("angriffsfilm") || which.StartsWith("treffer");
            _anatomy = which.StartsWith("anatomie") || which.StartsWith("rucke") || which.StartsWith("clips") || which.StartsWith("ziehfilm") || which.StartsWith("angriffsfilm");
            _allFindings.Clear();
            if (_orchestrator != null && weapons) _orchestrator.SetOverride(new CharacterMotionOrchestrator.IdentityOverride { Active = false });
            else if (_orchestrator != null)
                _orchestrator.SetOverride(new CharacterMotionOrchestrator.IdentityOverride
                {
                    Active = true, RaceId = RaceCatalog.HumanId,
                    Body = variant == "frau" ? BodyMotionVariant.Feminine : BodyMotionVariant.Masculine,
                    Role = ClassRole.Tank, Armor = ArmorCategory.Heavy, Weapon = WeaponFamily.SwordShield
                });
            yield return null;
            BindFeet();
            BindArms();
            if (_anatomy) BindAuditor();

            string tag = which.Replace(' ', '_');
            _report.Clear();
            _report.AppendLine($"ASHVEIL Motion-Lab Aufnahme „{which}“ · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _report.AppendLine(weapons
                ? $"Figur: {(variant == "frau" ? "Frau" : "Mann")} · echte Ausruestung je Abschnitt (Waffen-Parcours)"
                : $"Figur: {(variant == "frau" ? "Frau" : "Mann")} · Tank · schwer · Schwert+Schild (Ueberschreibung) · {(raw ? "ROH (ohne prozedurale Schicht)" : "mit prozeduraler Schicht")}");
            _report.AppendLine("Handgelenk: groesster Winkel Unterarm↔Hand im Abschnitt (natuerlich bis ~40°, Grenze 55°).");
            _report.AppendLine("Fussgleiten: Weg des stehenden Fusses waehrend Bodenkontakt (cm). Gut: < 3 cm, sichtbar ab ~6 cm.");
            _report.AppendLine("Gemessene Fussgelenke links/rechts: " + _feetBones);
            _report.AppendLine();

            foreach (Step s in Course(which))
            {
                _status = s.Title;
                yield return RunStep(s, tag);
            }

            if (_anatomy) { SetDead(false); Emotes()?.Stop(); AppendAnatomySummary(); }
            string path = Path.Combine(OutputDir, $"Motion-Aufnahme_{tag}.txt");
            File.WriteAllText(path, _report.ToString(), new UTF8Encoding(false));
            Time.captureDeltaTime = 0f;   // 22.10. (Codex): feste Bildzeit der fps-Aufnahmen nicht über die Aufnahme hinaus behalten
            Debug.Log("[Ashveil] Motion-Lab: Aufnahme fertig → " + path);
            _recording = false;
            _orchestrator?.ForceStance(null);
            Procedural.CharacterMotionPoser.GlobalEnabled = true;
            if (player != null) player.enabled = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private IEnumerator RunStep(Step s, string tag)
        {
            if (s.ClipAudit) { yield return ClipAudit(tag); yield break; }
            Emotes()?.Stop();
            // Standard: Erkundung (Waffe steckt); Abschnitte mit eigener Haltung behalten die bisherige (kein unnoetiges Wegstecken)
            if (s.Start == null) _orchestrator?.ForceStance(MotionStance.Exploration);
            if (s.Loadout != null)
            {
                Equip(s.Loadout);
                yield return new WaitForSeconds(0.3f);
            }
            Transform lane = Lane(s.Lane);
            Vector3 start = (lane != null ? lane.position : Vector3.zero) + s.Offset;
            float yaw = (lane != null ? lane.eulerAngles.y : 0f) + s.Yaw;
            puppet.Teleport(start, yaw);
            // Kurz stehen lassen, damit jeder Abschnitt aus derselben Ruhe beginnt; in der zweiten Hälfte die Ruhehöhe der
            // Ballen lernen (Bezug der Kontaktschwelle, 15.10.)
            _lTrack.Reset(); _rTrack.Reset();
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                yield return null; // Bewegen im selben Bild wie die Animation (siehe unten)
                puppet.Simulate(new MoveCommand { SteerWithCamera = true, DesiredYaw = yaw }, Time.deltaTime);
                yield return new WaitForEndOfFrame();
                if (t > 0.3f) SampleFeetRest();
            }
            FootTrack.ShareRest(_lTrack, _rTrack);
            if (s.Id == "22_sheathe" || s.PreCombat) { _orchestrator?.ForceStance(MotionStance.Combat); yield return new WaitForSeconds(1.4f); }
            s.Start?.Invoke();
            AnatomyAuditor audit = _anatomy ? _auditor : null;
            audit?.BeginStep();
            Procedural.AutoInertializer inert = _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Procedural.AutoInertializer>() : null;
            inert?.TakeStats(out _, out _);
            Texture2D evidence = null;

            int frames = Mathf.Max(1, s.Frames);
            bool around = s.Shot == Shot.Around;
            bool handsShot = s.Shot == Shot.Hands || s.Shot == Shot.Head || s.Shot == Shot.Kleid;   // Kopf, Kleid: gleiches 3×2-Raster
            int cols = around ? frames : handsShot ? frames * 3 : Mathf.Min(columns, frames);
            int rows = around ? AroundYaw.Length : handsShot ? 2 : Mathf.CeilToInt(frames / (float)columns);
            Procedural.WeaponHandPoser hp = _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Procedural.WeaponHandPoser>() : null;
            float wristR = 0f, wristL = 0f, gripErr = 0f, jump = 0f; string jumpInfo = "";
            hp?.TakeMaxWeaponJump();
            if (hp != null) hp.Trace = new StringBuilder("phase;t;dt_ms;sprung_roh_cm;waffe_x;waffe_y;waffe_z;hand_x;hand_y;hand_z;ziel_x;ziel_y;ziel_z;gewicht;rumpfhilfe;eltern;hand_ziel_cm;hand_ziel_grad;hand_dreh_grad_bild;pronation;schwenk\n");
            var sheet = new Texture2D(tileWidth * cols, tileHeight * rows, TextureFormat.RGB24, false);
            var rt = new RenderTexture(tileWidth, tileHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tile = new Texture2D(tileWidth, tileHeight, TextureFormat.RGB24, false);
            captureCamera.targetTexture = rt;
            captureCamera.enabled = false;

            _lTrack.Reset(true); _rTrack.Reset(true);
            ResetArms(); BeginSkeleton();
            Verzug(true);
            _feetTrace = new StringBuilder("t;dt_ms;koerper_ms;links_hoehe_cm;links_ms;links_kontakt;rechts_hoehe_cm;rechts_ms;rechts_kontakt;tempo;laufband_ms\n"); _feetT = 0f; _poser = null;
            float total = s.Lead + s.Duration;
            int shot = 0;
            Vector3 camAnchor = puppet.transform.position;
            float camYaw = yaw;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                MoveCommand cmd = s.Command(t - s.Lead); // Zeit ab Messbeginn (vorher negativ)
                if (cmd.SteerWithCamera) cmd.DesiredYaw += yaw;
                // 15.10. (3): Erst ins naechste Bild (nach Update, VOR der Animation), dann mit DESSEN Bildzeit bewegen. Vorher
                // bewegte das Lab die Figur am Bildende mit der Zeit des alten Bildes, die Animation lief aber mit der neuen –
                // bei wechselnden Bildzeiten (7/14 ms, Bildaufnahmen 40–70 ms) lief der stehende Fuss scheinbar mit ±1 m/s,
                // Gleiten und „Waffensprung“ wurden ueberschaetzt. Im Spiel bewegt der Motor im selben Bild wie die Animation.
                yield return null;
                puppet.Simulate(cmd, Time.deltaTime);
                yield return new WaitForEndOfFrame();
                SampleFeet(t >= s.Lead);
                if (t >= s.Lead) { SampleArms(); SampleSkeleton(t - s.Lead); }
                if (audit != null && audit.NewWorst) { evidence = Evidence(evidence); audit.MarkCaptured(); }
                if (t >= s.Lead && hp != null) { wristR = Mathf.Max(wristR, hp.RightWrist); wristL = Mathf.Max(wristL, hp.LeftWrist); gripErr = Mathf.Max(gripErr, hp.LeftGripError); float j = hp.TakeMaxWeaponJump(out string ji); if (j > jump) { jump = j; jumpInfo = ji; } }

                // Aufnahme-Zeitpunkte gleichmaessig ueber die Messzeit verteilt
                if (t >= s.Lead)
                {
                    float local = t - s.Lead;
                    float next = shot * (s.Duration / frames);
                    if (shot < frames && local >= next)
                    {
                        int views = around ? AroundYaw.Length : handsShot ? 6 : 1;
                        for (int v = 0; v < views; v++)
                        {
                            if (around) PlaceAround(AroundYaw[v]);
                            else if (s.Shot == Shot.Head) PlaceHead(v);
                            else if (s.Shot == Shot.Kleid) PlaceKleid(v);
                            else if (handsShot) PlaceHands(v);
                            else PlaceCamera(s.Shot, camAnchor, camYaw);
                            captureCamera.Render();
                            RenderTexture prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            tile.ReadPixels(new Rect(0, 0, tileWidth, tileHeight), 0, 0);
                            tile.Apply(false);
                            RenderTexture.active = prev;
                            int col = around ? shot : handsShot ? shot * 3 + v % 3 : shot % columns;
                            int row = around ? rows - 1 - v : handsShot ? rows - 1 - v / 3 : rows - 1 - shot / columns;
                            sheet.SetPixels(col * tileWidth, row * tileHeight, tileWidth, tileHeight, tile.GetPixels());
                        }
                        shot++;
                    }
                }
            }
            captureCamera.targetTexture = null;
            sheet.Apply(false);
            string file = Path.Combine(OutputDir, $"{tag}_{s.Id}.png");
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet); Destroy(tile); rt.Release(); Destroy(rt);

            _lTrack.Close(); _rTrack.Close();
            if (_feetTrace != null) { File.WriteAllText(Path.Combine(OutputDir, $"{tag}_{s.Id}_fuesse.csv"), _feetTrace.ToString(), new UTF8Encoding(false)); _feetTrace = null; }
            if (_skelTrace != null) { File.WriteAllText(Path.Combine(OutputDir, $"{tag}_{s.Id}_skelett.csv"), _skelTrace.ToString(), new UTF8Encoding(false)); _skelTrace = null; }
            // Mitschrift Ziehen/Wegstecken je Bild (nur wenn etwas choreografiert wurde)
            if (hp != null && hp.Trace != null)
            {
                if (hp.Trace.Length > 200) File.WriteAllText(Path.Combine(OutputDir, $"{tag}_{s.Id}_waffe.csv"), hp.Trace.ToString(), new UTF8Encoding(false));
                hp.Trace = null;
            }
            if (audit != null)
            {
                if (evidence != null)
                {
                    if (audit.Count > 0) File.WriteAllBytes(Path.Combine(OutputDir, $"{tag}_{s.Id}_befund.png"), evidence.EncodeToPNG());
                    Destroy(evidence);
                }
                Collect(s.Id, audit);
            }
            _report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-18} {1,-34} Fussgleiten links {2,5:0.0}/{3,5:0.0} cm · rechts {4,5:0.0}/{5,5:0.0} cm (mittel/max) · Kontakte {6}/{7} · im Boden max {8:0.0} cm",
                s.Id, s.Title, _lTrack.MeanDrift * 100f, _lTrack.MaxDrift * 100f, _rTrack.MeanDrift * 100f, _rTrack.MaxDrift * 100f,
                _lTrack.Contacts, _rTrack.Contacts, Mathf.Max(_lTrack.MaxPenetration, _rTrack.MaxPenetration) * 100f)
                // 15.10.: Belege für „schwebt / steckt / Messfehler“ – Ballen in Kontaktbildern über seiner Ruhehöhe, Ruhehöhe,
                // und wo/worauf der tiefste Punkt gemessen wurde
                + string.Format(CultureInfo.InvariantCulture, " · Ballen im Kontakt ueber Ruhe Ø links {0:0.0} rechts {1:0.0} cm (Ruhe {2:0.0}/{3:0.0} cm)",
                    _lTrack.MeanContactLift * 100f, _rTrack.MeanContactLift * 100f, Mathf.Max(0f, _lTrack.RestHeight) * 100f, Mathf.Max(0f, _rTrack.RestHeight) * 100f)
                + string.Format(CultureInfo.InvariantCulture, " · Kontaktbilder links {0} rechts {1}, tief aber zu schnell (nicht gezaehlt) links {2} (Ø {3:0.0} m/s) rechts {4} (Ø {5:0.0} m/s)",
                    _lTrack.ContactFrames, _rTrack.ContactFrames, _lTrack.LowFastFrames, _lTrack.LowFastMeanSpeed, _rTrack.LowFastFrames, _rTrack.LowFastMeanSpeed)
                + string.Format(CultureInfo.InvariantCulture, " · tiefster Ballen links {0:0.0} rechts {1:0.0} cm",
                    (_lTrack.MinHeight < 1e6f ? _lTrack.MinHeight : 0f) * 100f, (_rTrack.MinHeight < 1e6f ? _rTrack.MinHeight : 0f) * 100f)
                + (Mathf.Max(_lTrack.MaxPenetration, _rTrack.MaxPenetration) > 0.01f
                    ? string.Format(CultureInfo.InvariantCulture, " (tiefster Punkt {0} t={1:0.00} s auf '{2}')",
                        _lTrack.MaxPenetration >= _rTrack.MaxPenetration ? "links" : "rechts",
                        _lTrack.MaxPenetration >= _rTrack.MaxPenetration ? _lTrack.PenetrationTime : _rTrack.PenetrationTime,
                        _lTrack.MaxPenetration >= _rTrack.MaxPenetration ? _lTrack.PenetrationHit : _rTrack.PenetrationHit)
                    : "")
                + (hp != null ? string.Format(CultureInfo.InvariantCulture, " · Handgelenk rechts {0:0}° links {1:0}° · Haltung {2} · linke Hand neben dem Griff {3:0.0} cm · groesster Sprung der Waffe {4:0.0} cm{5}", wristR, wristL, hp.Style, gripErr * 100f, jump * 100f, jump > 0.03f ? " (" + jumpInfo + ")" : "") : "")
                + ArmsReport()
                + (hp != null ? " · Handposer " + hp.DebugState : "")
                + (_stancePoser != null ? string.Format(CultureInfo.InvariantCulture, " · Standhaltung Gewicht {0:0.00} Waffe-steckt {1} ArmEigen L {2:0.00} R {3:0.00}", _stancePoser.Weight, _stancePoser.WeaponSheathed, _stancePoser.LeftArmOwn, _stancePoser.RightArmOwn) : "")
                + Verzug(false));
            if (hp != null && hp.Grip.Valid && hp.Style != Procedural.GripStyle.Unarmed && (s.Loadout != null || s.Id.EndsWith("_2_ziehen")))
                _report.Append(string.Format(CultureInfo.InvariantCulture, "      Griff vermessen: {0} · Faust bei {1:0.000} m · Griff {2:0.00}…{3:0.00} m · Dicke Ø {4:0.0} cm{5}\n",
                    hp.Grip.Note, hp.Grip.Right.z, hp.Grip.HandleMin, hp.Grip.HandleMax, hp.Grip.Radius * 200f, hp.Grip.HasSupport ? " · Stuetzhand unter dem Vorderschaft" : hp.Grip.HasLeft ? " · zweite Hand am Griff" : ""));
            if (audit != null) _report.Append(audit.Count == 0 ? "      ✓ anatomisch sauber\n" : audit.Report());
            // 18.10.: Kopf-Aufnahme listet alle sichtbaren Renderer der Figur mit Material und Texturen (Herkunft dunkler Flecken)
            // 21.10.: ebenso die erste Kleidungs-Nahaufnahme, dazu die Ausrüstung des AppearanceRig (Kleid oder Schalen, Haut verdeckt)
            if (s.Shot == Shot.Head || s.Id == "k01_nah_ruhe")
            {
                Animator fa = ActiveAnimator();
                Transform root = fa != null ? fa.transform.root : null;
                Ashveil.Gameplay.Visuals.AppearanceRig ar = fa != null ? fa.GetComponentInChildren<Ashveil.Gameplay.Visuals.AppearanceRig>() : null;
                if (ar == null && root != null) ar = root.GetComponentInChildren<Ashveil.Gameplay.Visuals.AppearanceRig>();
                if (ar != null)
                    _report.AppendLine("      Ausruestung " + ar.Summary() + " · Koerper " + (ar.Body != null ? ar.Body.name : "-") + " · Haut verdeckt " + ar.BodyMasked);
                LODGroup lg = root != null ? root.GetComponentInChildren<LODGroup>() : null;
                if (lg != null)
                {
                    var sbl = new StringBuilder("      LODGroup " + lg.name + ":");
                    LOD[] ls = lg.GetLODs();
                    for (int i = 0; i < ls.Length; i++)
                        foreach (Renderer r in ls[i].renderers)
                            if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null)
                                sbl.Append(string.Format(CultureInfo.InvariantCulture, " [{0} ab {1:0.000}: {2} {3} Dreiecke]", i, ls[i].screenRelativeTransitionHeight, sk.name, sk.sharedMesh.triangles.Length / 3));
                    _report.AppendLine(sbl.ToString());
                }
                if (root != null)
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!r.enabled) continue;
                        var sb2 = new StringBuilder("      Renderer " + r.name + " (" + r.GetType().Name + (r is SkinnedMeshRenderer sk && sk.sharedMesh != null ? ", Mesh " + sk.sharedMesh.name : "") + "):");
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m == null) { sb2.Append(" [null]"); continue; }
                            Texture bm = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                            Texture nm = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                            sb2.Append(" [" + m.name + " | " + m.shader.name + " | Basis " + (bm != null ? bm.name : "-") + " | Normal " + (nm != null ? nm.name : "-")
                                + (m.HasProperty("_BumpScale") ? string.Format(CultureInfo.InvariantCulture, " x{0:0.00}", m.GetFloat("_BumpScale")) : "") + "]");
                        }
                        _report.AppendLine(sb2.ToString());
                    }
            }
            if (audit != null && inert != null)
            {
                inert.TakeStats(out int smoothed, out float largest);
                if (smoothed > 0) _report.Append(string.Format(CultureInfo.InvariantCulture, "      ~ nahtlos geglaettet: {0} Knochen-Spruenge (groesster vorher {1:0.0}°)\n", smoothed, largest));
            }
        }

        private void PlaceCamera(Shot shot, Vector3 anchor, float yaw)
        {
            Vector3 p = puppet.transform.position;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = 26f;
            Vector3 focus = p + Vector3.up * 0.95f;
            switch (shot)
            {
                case Shot.Side:
                    captureCamera.transform.position = focus + right * 7.5f + Vector3.up * 0.2f;
                    break;
                case Shot.Front:
                    captureCamera.transform.position = focus + fwd * 7.5f + Vector3.up * 0.3f;
                    break;
                case Shot.Top:
                    captureCamera.fieldOfView = 34f;
                    focus = anchor + Vector3.up * 0.9f + fwd * 3f + right * 1.5f;
                    captureCamera.transform.position = focus + Vector3.up * 11f - fwd * 4f;
                    break;
                default:
                    captureCamera.transform.position = focus + (right * 0.8f + fwd * 0.6f).normalized * 6f + Vector3.up * 1.2f;
                    break;
            }
            captureCamera.transform.LookAt(focus);
        }

        /// <summary>Kamera um die Figur: 0 = von vorn, 90 = von rechts, 180 = von hinten, 270 = von links (leicht erhoeht).</summary>
        private void PlaceAround(float angle)
        {
            Vector3 p = puppet.transform.position;
            Quaternion rot = Quaternion.Euler(0f, puppet.transform.eulerAngles.y + angle, 0f);
            Vector3 focus = p + Vector3.up * 0.95f;
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = 24f;
            captureCamera.transform.position = focus + rot * Vector3.forward * 6.5f + Vector3.up * 0.9f;
            captureCamera.transform.LookAt(focus);
        }

        /// <summary>18.10.: Nahaufnahme des Kopfes (Gesicht, Haare): vorn, ¾ rechts, rechts, hinten, ¾ links, Brustbild vorn.</summary>
        private void PlaceHead(int view)
        {
            Animator anim = _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Animator>() : null;
            Transform head = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform t = puppet.transform;
            Vector3 focus = head != null ? head.position + Vector3.up * 0.09f : t.position + Vector3.up * 1.7f;
            float[] yaws = { 0f, 40f, 90f, 180f, -40f, 0f };
            // Blickrichtung aus den Schultern (rechts × oben = vorn) – die Ausrichtung des Lab-Objekts wechselte zwischen den Läufen
            Transform ls = anim != null ? anim.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null, rs = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            Vector3 vorn = ls != null && rs != null ? Vector3.Cross(Vector3.ProjectOnPlane(rs.position - ls.position, Vector3.up).normalized, Vector3.up) : t.forward;
            Vector3 dir = Quaternion.AngleAxis(yaws[view], Vector3.up) * vorn + Vector3.up * 0.08f;
            float dist = view == 5 ? 1.6f : 0.75f;
            if (view == 5) focus -= Vector3.up * 0.35f;
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = 28f;
            captureCamera.transform.position = focus + dir.normalized * dist;
            captureCamera.transform.LookAt(focus);
        }

        /// <summary>
        /// 21.10.2026 (Tripo-Kleidung): Nahaufnahmen der Kleidung – 0 Schulter rechts (vorn aussen), 1 Achsel links (vorn, leicht von
        /// unten), 2 Ruecken/Kragen (hinten oben), 3 Schritt (vorn), 4 Knie rechts (seitlich), 5 Saum (hinten). Blickrichtung aus den
        /// Schultern wie bei <see cref="PlaceHead"/>.
        /// </summary>
        private void PlaceKleid(int view)
        {
            Animator anim = _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Animator>() : null;
            Transform B(HumanBodyBones b) => anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;
            Transform t = puppet.transform;
            Vector3 P(HumanBodyBones b, Vector3 ersatz) { Transform x = B(b); return x != null ? x.position : ersatz; }
            Transform ls = B(HumanBodyBones.LeftUpperArm), rs = B(HumanBodyBones.RightUpperArm);
            Vector3 vorn = ls != null && rs != null ? Vector3.Cross(Vector3.ProjectOnPlane(rs.position - ls.position, Vector3.up).normalized, Vector3.up) : t.forward;
            Vector3 rechts = Vector3.Cross(Vector3.up, vorn);
            Vector3 hips = P(HumanBodyBones.Hips, t.position + Vector3.up);
            Vector3 focus, dir;
            float dist;
            switch (view)
            {
                case 0: focus = P(HumanBodyBones.RightUpperArm, hips + Vector3.up * 0.4f) - Vector3.up * 0.04f; dir = vorn * 0.6f + rechts * 0.75f + Vector3.up * 0.25f; dist = 0.75f; break;
                case 1: focus = P(HumanBodyBones.LeftUpperArm, hips + Vector3.up * 0.4f) - Vector3.up * 0.13f + rechts * 0.04f; dir = vorn * 0.85f - rechts * 0.45f - Vector3.up * 0.2f; dist = 0.75f; break;
                case 2: focus = P(HumanBodyBones.Neck, hips + Vector3.up * 0.45f) - Vector3.up * 0.12f; dir = -vorn * 0.85f + rechts * 0.3f + Vector3.up * 0.35f; dist = 0.9f; break;
                case 3: focus = hips - Vector3.up * 0.14f; dir = vorn + Vector3.up * 0.08f; dist = 0.95f; break;
                case 4: focus = P(HumanBodyBones.RightLowerLeg, hips - Vector3.up * 0.45f); dir = rechts * 0.85f + vorn * 0.45f + Vector3.up * 0.1f; dist = 0.8f; break;
                default: focus = hips - Vector3.up * 0.2f; dir = -vorn + Vector3.up * 0.05f; dist = 1.1f; break;
            }
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = 32f;
            captureCamera.transform.position = focus + dir.normalized * dist;
            captureCamera.transform.LookAt(focus);
        }

        /// <summary>Nahaufnahme der Haende: 0–2 rechte Hand (vorn, aussen, oben), 3–5 linke Hand.</summary>
        private void PlaceHands(int view)
        {
            Animator anim = _bodies != null && _bodies.ActiveModel != null ? _bodies.ActiveModel.GetComponent<Animator>() : null;
            bool left = view >= 3;
            Transform hand = anim != null ? anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : null;
            Transform mid = anim != null ? anim.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal) : null;
            Transform t = puppet.transform;
            Vector3 focus = hand != null ? (mid != null ? (hand.position + mid.position) * 0.5f : hand.position) : t.position + Vector3.up;
            Vector3 side = left ? -t.right : t.right;
            Vector3 dir;
            switch (view % 3)
            {
                case 0: dir = (t.forward * 0.85f + side * 0.35f + Vector3.up * 0.15f); break;   // vorn
                case 1: dir = (side * 0.9f + t.forward * 0.15f + Vector3.up * 0.1f); break;      // aussen
                default: dir = (Vector3.up * 0.9f + t.forward * 0.3f + side * 0.15f); break;     // oben
            }
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = 30f;
            captureCamera.transform.position = focus + dir.normalized * 0.55f;
            captureCamera.transform.LookAt(focus, Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.8f ? t.forward : Vector3.up);
        }

        // ================================================================== Anatomie

        private bool _anatomy;
        private string _clipFilter;
        private AnatomyAuditor _auditor;
        private readonly Dictionary<string, (float over, string step, string text)> _allFindings = new Dictionary<string, (float, string, string)>();

        private void BindAuditor()
        {
            Animator a = ActiveAnimator();
            if (a == null) return;
            _auditor = a.GetComponent<AnatomyAuditor>();
            if (_auditor == null) _auditor = a.gameObject.AddComponent<AnatomyAuditor>();
            // 16.10. abends: je Ruck die Stelle im Animator mitschreiben (Clip-Zeit 0–1 der Basis-Schicht, Wiedergabetempo,
            // aktive Schichten) – damit sieht man, ob die Rucke an der Schleifen-Naht liegen oder woanders
            {
                Animator ka = a;
                _auditor.Kontext = () =>
                {
                    if (ka == null || !ka.isActiveAndEnabled || ka.layerCount == 0) return "";
                    var st = ka.GetCurrentAnimatorStateInfo(0);
                    var sb = new StringBuilder();
                    sb.Append("nt=").Append((st.normalizedTime % 1f).ToString("0.00", CultureInfo.InvariantCulture));
                    sb.Append(" len=").Append(st.length.ToString("0.00", CultureInfo.InvariantCulture));
                    sb.Append(" spd=").Append((ka.speed * st.speedMultiplier).ToString("0.00", CultureInfo.InvariantCulture));
                    if (ka.IsInTransition(0)) sb.Append(" uebergang");
                    for (int li = 1; li < ka.layerCount; li++) { float w = ka.GetLayerWeight(li); if (w > 0.01f) sb.Append(" L").Append(li).Append('=').Append(w.ToString("0.0", CultureInfo.InvariantCulture)); }
                    return sb.ToString();
                };
            }
            _auditor.Bind(a);
            _report.AppendLine("Anatomie: jedes Gelenk in jedem Bild gegen menschliche Grenzen (Toleranz 3°). ✗ = ausserhalb; Ruck = sprunghafte Winkelbeschleunigung als Stufe (8 = sichtbar; Arme ab 12°/Bild², Beine ab 40°/Bild² bei 60 Bildern/s – Fussaufsatz ist ein echter Stopp).");
            _report.AppendLine();
        }

        private void Collect(string step, AnatomyAuditor audit)
        {
            foreach (AnatomyAuditor.Finding f in audit.Findings())
            {
                string key = f.Joint + " " + f.What;
                if (!_allFindings.TryGetValue(key, out var old) || f.Over > old.over)
                    _allFindings[key] = (f.Over, step, f.What == "cm"
                        ? string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} cm", f.Joint, f.Value)
                        : string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2:0}° (Grenze {3:0}°)", f.Joint, f.What, f.Value, f.Limit));
            }
            float jerk = audit.MaxJerk(out string bone);
            if (jerk >= 8f)
            {
                string key = "Ruck " + bone;
                if (!_allFindings.TryGetValue(key, out var old) || jerk > old.over)
                    _allFindings[key] = (jerk, step, string.Format(CultureInfo.InvariantCulture, "Ruck {0}: Stufe {1:0.0}", bone, jerk));
            }
        }

        private void AppendAnatomySummary()
        {
            var list = new List<KeyValuePair<string, (float over, string step, string text)>>(_allFindings);
            list.Sort((a, b) => b.Value.over.CompareTo(a.Value.over));
            _report.AppendLine();
            _report.AppendLine($"ZUSAMMENFASSUNG Anatomie: {list.Count} verschiedene Befunde (schlimmster Fall je Gelenk/Richtung)");
            foreach (var kv in list) _report.AppendLine($"  {kv.Value.text}  ←  {kv.Value.step}");
        }

        /// <summary>Beweisbild im Moment des schlimmsten Verstosses: vorn, rechts, links, hinten (nebeneinander).</summary>
        private Texture2D Evidence(Texture2D sheet)
        {
            if (sheet == null) sheet = new Texture2D(tileWidth * 4, tileHeight, TextureFormat.RGB24, false);
            RenderTexture target = captureCamera.targetTexture;
            var rt = new RenderTexture(tileWidth, tileHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tile = new Texture2D(tileWidth, tileHeight, TextureFormat.RGB24, false);
            captureCamera.targetTexture = rt;
            float[] yaws = { 0f, 90f, 270f, 180f };
            for (int v = 0; v < 4; v++)
            {
                PlaceAround(yaws[v]);
                captureCamera.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                tile.ReadPixels(new Rect(0, 0, tileWidth, tileHeight), 0, 0);
                tile.Apply(false);
                RenderTexture.active = prev;
                sheet.SetPixels(v * tileWidth, 0, tileWidth, tileHeight, tile.GetPixels());
            }
            sheet.Apply(false);
            captureCamera.targetTexture = target;
            Destroy(tile); rt.Release(); Destroy(rt);
            return sheet;
        }

        /// <summary>
        /// Jeder Clip des Animators (Spieler-Controller bzw. Frauen-Variante) Bild fuer Bild mit 60 Bildern/s abgetastet,
        /// ohne prozedurale Schichten: Gelenkgrenzen, Rucke und die Naht von Schleifen (Ende → Anfang).
        /// </summary>
        private IEnumerator ClipAudit(string tag)
        {
            Animator anim = ActiveAnimator();
            if (anim == null || _auditor == null || anim.runtimeAnimatorController == null) yield break;
            _report.AppendLine("00_clips          Alle Animationsclips Bild fuer Bild (rohe Clips, ohne prozedurale Schicht)");
            RuntimeAnimatorController ctrl = anim.runtimeAnimatorController;
            var clips = new List<AnimationClip>();
            var seen = new HashSet<string>();
            foreach (AnimationClip c in ctrl.animationClips) if (c != null && seen.Add(c.name) && (_clipFilter == null || c.name.Contains(_clipFilter))) clips.Add(c);
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var paused = new List<Behaviour>();
            foreach (Behaviour b in anim.GetComponents<Behaviour>())
                if (b != anim && b != _auditor && b.enabled) { b.enabled = false; paused.Add(b); }
            bool root = anim.applyRootMotion;
            anim.applyRootMotion = false;
            anim.runtimeAnimatorController = null;
            _auditor.AutoMeasure = false;
            Vector3 pos = puppet.transform.position;

            PlayableGraph graph = PlayableGraph.Create("Ashveil-Anatomie");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Anatomie", anim);
            var bones = new List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) { Transform t = anim.GetBoneTransform((HumanBodyBones)i); if (t != null) bones.Add(t); }
            var first = new Quaternion[bones.Count];
            RenderTexture camTarget = captureCamera.targetTexture;
            foreach (AnimationClip clip in clips)
            {
                _status = "Clip " + clip.name;
                var cp = AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(false);
                output.SetSourcePlayable(cp);
                _auditor.BeginStep();
                float len = Mathf.Max(clip.length, 1f / 60f);
                int n = Mathf.Max(2, Mathf.CeilToInt(len * 60f));
                float dt = len / n;
                int extra = clip.isLooping ? 3 : 0; // ueber die Naht hinaus weiter abtasten
                float seam = 0f; string seamBone = null;
                // 16.10. abends: Naht-Mitschrift – Schrittwinkel von Unterarm und Becken um die Naht herum (Bild n-2 … 2)
                Transform nahtArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm), nahtBecken = anim.GetBoneTransform(HumanBodyBones.Hips);
                Quaternion armVor = Quaternion.identity, beckenVor = Quaternion.identity; var nahtText = new StringBuilder();
                for (int i = 0; i <= n + extra; i++)
                {
                    float t = i <= n ? dt * i : dt * (i - n);
                    cp.SetTime(t);
                    graph.Evaluate(0f);
                    _auditor.Measure(i == 0 ? 0f : dt);
                    if (clip.isLooping && nahtArm != null && nahtBecken != null && i >= n - 3)
                    {
                        if (i > n - 3) nahtText.Append(string.Format(CultureInfo.InvariantCulture, "{0}→{1}: Arm {2:0.0}° Becken {3:0.0}° | ", i - 1 <= n ? (i - 1).ToString() : "+" + (i - 1 - n), i <= n ? i.ToString() : "+" + (i - n), Quaternion.Angle(armVor, nahtArm.localRotation), Quaternion.Angle(beckenVor, nahtBecken.localRotation)));
                        armVor = nahtArm.localRotation; beckenVor = nahtBecken.localRotation;
                    }
                    if (i == 0) for (int b = 0; b < bones.Count; b++) first[b] = bones[b].localRotation;
                    if (i == n && clip.isLooping)
                        for (int b = 0; b < bones.Count; b++)
                        {
                            float a = Quaternion.Angle(first[b], bones[b].localRotation);
                            if (a > seam) { seam = a; seamBone = bones[b].name; }
                        }
                }
                List<AnatomyAuditor.Finding> found = _auditor.Findings();
                float jerk = _auditor.MaxJerk(out string jb);
                bool bad = found.Count > 0 || seam > 4f || jerk >= 8f;
                _report.Append(string.Format(CultureInfo.InvariantCulture, "   Clip {0,-28} {1,5:0.00} s{2}{3}\n", clip.name, clip.length, clip.isLooping ? " · Schleife" : "",
                    clip.isLooping ? string.Format(CultureInfo.InvariantCulture, " · Naht {0:0.0}° ({1})", seam, seamBone) : ""));
                _report.Append(bad ? _auditor.Report(8) : "      ✓ anatomisch sauber\n");
                if (clip.isLooping && seam > 4f) _report.Append(string.Format(CultureInfo.InvariantCulture, "      ✗ Schleifen-Naht springt {0:0.0}° ({1})\n", seam, seamBone));
                if (nahtText.Length > 0) _report.Append("      Naht-Schritte (n = " + n + ", Bild " + (1f / dt).ToString("0.0", CultureInfo.InvariantCulture) + "/s): " + nahtText + "\n");
                // 16.10. spaet: Feinabtastung (Viertelbilder) ueber die Naht – im Spiel (145 Bilder/s) rucken die Unterarme bei
                // Clip-Zeit 0,02, also INNERHALB des ersten Bildintervalls; bei Abtastung genau auf den Bildern ist nichts zu sehen.
                // Verdacht: Kurven-Tangenten am ersten/letzten Bild sind nicht schleifenbewusst (abgeflacht).
                if (clip.isLooping && nahtArm != null)
                {
                    var fein = new StringBuilder(); Quaternion vor = Quaternion.identity; bool erst = true;
                    for (int k = -8; k <= 8; k++)
                    {
                        float t = Mathf.Repeat(clip.length + k * dt * 0.25f, clip.length);
                        cp.SetTime(t); graph.Evaluate(0f);
                        Quaternion q = nahtArm.localRotation;
                        if (!erst) fein.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.0} ", Quaternion.Angle(vor, q)));
                        vor = q; erst = false;
                    }
                    _report.Append("      Naht fein (Unterarm, Viertelbild-Schritte von -2 bis +2 Bilder um die Naht): " + fein + "\n");
                    // Einzelpunkte: Pose bei 0, kurz nach 0, kurz vor Ende, Ende – und die Nahtschritte ohne Mathf.Repeat (Playable wickelt selbst um)
                    Quaternion P(float t) { cp.SetTime(t); graph.Evaluate(0f); return nahtArm.localRotation; }
                    Quaternion qEnde = P(clip.length), qEnde1 = P(clip.length - 0.0005f), q0 = P(0f), q01 = P(0.0005f), qDt = P(dt), qNeg = P(-0.0005f), qLen1 = P(clip.length + 0.0005f);
                    _report.Append(string.Format(CultureInfo.InvariantCulture, "      Naht Punkte: Ende→0 {0:0.0}° · Ende-½ms→Ende {1:0.0}° · 0→½ms {2:0.0}° · 0→1 Bild {3:0.0}° · -½ms→0 {4:0.0}° · Ende→Ende+½ms {5:0.0}° · Ende+½ms→½ms {6:0.0}°\n",
                        Quaternion.Angle(qEnde, q0), Quaternion.Angle(qEnde1, qEnde), Quaternion.Angle(q0, q01), Quaternion.Angle(q0, qDt), Quaternion.Angle(qNeg, q0), Quaternion.Angle(qEnde, qLen1), Quaternion.Angle(qLen1, q01)));
                    var fein2 = new StringBuilder(); vor = Quaternion.identity; erst = true;
                    for (int k = -8; k <= 8; k++)
                    {
                        Quaternion q = P(clip.length + k * dt * 0.25f);
                        if (!erst) fein2.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.0} ", Quaternion.Angle(vor, q)));
                        vor = q; erst = false;
                    }
                    _report.Append("      Naht fein ohne Umwickeln (Zeit laeuft ueber das Ende hinaus): " + fein2 + "\n");
                }
                Collect("Clip " + clip.name, _auditor);
                if (found.Count > 0)
                {
                    // Beweisbild im schlimmsten Moment
                    cp.SetTime(found[0].Time);
                    graph.Evaluate(0f);
                    puppet.transform.position = pos;
                    Texture2D ev = Evidence(null);
                    File.WriteAllBytes(Path.Combine(OutputDir, $"{tag}_clip_{Safe(clip.name)}_befund.png"), ev.EncodeToPNG());
                    Destroy(ev);
                }
                cp.Destroy();
                yield return null;
            }
            graph.Destroy();
            captureCamera.targetTexture = camTarget;
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = root;
            foreach (Behaviour b in paused) if (b != null) b.enabled = true;
            _auditor.AutoMeasure = true;
            _auditor.ResetMotionHistory();
            _report.AppendLine();
            yield return new WaitForSeconds(0.5f);
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        private Transform Lane(string name)
        {
            if (lanes == null) return null;
            foreach (Transform t in lanes) if (t != null && t.name == name) return t;
            return null;
        }

        // ================================================================== Fussmessung

        private sealed class FootTrack
        {
            public float MaxDrift, SumDrift, MaxPenetration;
            public int Contacts;
            public float MinHeight = float.MaxValue;
            private bool _planted;
            private Vector3 _plantPos;
            private float _episodeDrift;

            public float MeanDrift => Contacts > 0 ? SumDrift / Contacts : 0f;

            /// <summary>
            /// Höhe des Zehen-/Ballengelenks im ruhigen Stand vor dem Abschnitt (m). 15.10.: Kontaktschwelle bezieht sich darauf
            /// statt auf „5 cm absolut“ bzw. das Minimum des Abschnitts – das Ballengelenk liegt je Figur 2–5 cm über dem Boden,
            /// ein um 2 cm schwebender Fuss fiel sonst ganz aus der Messung („0 Kontakte“ statt „schwebt 2 cm“).
            /// </summary>
            public float RestHeight = -1f;
            /// <summary>Letztes Bild (Fuss-Mitschrift): Hoehe, waagerechte Geschwindigkeit, als Kontakt gezaehlt.</summary>
            public float LastHeight, LastSpeed; public bool LastContact;
            private int _restN;
            /// <summary>Mittlere Zehenhöhe über der Ruhehöhe in Kontaktbildern (cm-Auswertung: schwebt / steckt).</summary>
            public float MeanContactLift => _contactFrames > 0 ? _contactLiftSum / _contactFrames : 0f;
            public int ContactFrames => _contactFrames;
            public int LowFastFrames;
            private float _lowFastSpeedSum;
            public float LowFastMeanSpeed => LowFastFrames > 0 ? _lowFastSpeedSum / LowFastFrames : 0f;
            private float _contactLiftSum; private int _contactFrames;
            /// <summary>Tiefster Messpunkt: Zeit im Abschnitt und getroffener Collider (Messfehler vs. echtes Eindringen).</summary>
            public float PenetrationTime; public string PenetrationHit = "";
            private float _time;

            public void Reset(bool keepRest = false)
            {
                MaxDrift = SumDrift = MaxPenetration = 0f;
                Contacts = 0;
                _hasLast = false;
                MinHeight = float.MaxValue;
                _planted = false;
                if (!keepRest) { RestHeight = -1f; _restN = 0; }
                _contactLiftSum = 0f; _contactFrames = 0; LowFastFrames = 0; _lowFastSpeedSum = 0f;
                PenetrationTime = 0f; PenetrationHit = ""; _time = 0f;
            }

            /// <summary>
            /// 15.10. (3): tiefster Wert statt Mittel – setzt die Figur im Ruhefenster noch einen Schritt (nach dem Versetzen),
            /// zog der gehobene Fuss das Mittel bis an die 10-cm-Grenze; die Kontaktschwelle lag dann bei 13,5 cm und jeder
            /// Schritt im Stand zaehlte als „Gleiten“ (Drehen mit Tasten 47 cm, Anhalten 15 cm). Ein stehender Ballen ist
            /// der tiefste Wert im Fenster.
            /// </summary>
            public void SampleRest(float height)
            {
                _restN++;
                RestHeight = _restN == 1 ? Mathf.Clamp(height, 0f, 0.1f) : Mathf.Min(RestHeight, Mathf.Clamp(height, 0f, 0.1f));
            }

            /// <summary>Beide Ballen sind gleich gebaut: gemeinsame Ruhehoehe = die tiefere der beiden (ein Fuss kann im
            /// ganzen Fenster in der Luft gewesen sein).</summary>
            public static void ShareRest(FootTrack a, FootTrack b)
            {
                if (a.RestHeight < 0f || b.RestHeight < 0f) return;
                float r = Mathf.Min(a.RestHeight, b.RestHeight);
                a.RestHeight = r; b.RestHeight = r;
            }

            private Vector3 _last;
            private bool _hasLast;

            public void Sample(Vector3 pos, float height, bool measure, float dt, float bodySpeed, string hitName = "")
            {
                _time += dt;
                MinHeight = Mathf.Min(MinHeight, height);
                float pen = -Mathf.Min(0f, height + 0.005f);
                if (measure && pen > MaxPenetration) { MaxPenetration = pen; PenetrationTime = _time; PenetrationHit = hitName; }
                Vector3 v = _hasLast && dt > 0f ? (pos - _last) / dt : Vector3.zero; v.y = 0f;
                _last = pos; _hasLast = true;
                // Bodenkontakt: tief UND deutlich langsamer als der Koerper (sonst ist es die Schwungphase knapp ueber dem Boden)
                // Im Stand (Drehen, Anhalten) zaehlt jede Bewegung eines tiefen Fusses als Gleiten – dort gibt es keine Schwungphase
                // 15.10. (2): „tief“ = hoechstens 3,5 cm ueber der Ruhehoehe des Ballens (vorher max(MinHeight + 3,5; 5 cm) absolut).
                float low = RestHeight >= 0f ? RestHeight + 0.035f : Mathf.Max(MinHeight + 0.035f, 0.05f);
                bool contact = height < low && (bodySpeed < 0.3f || v.magnitude < Mathf.Max(0.6f, bodySpeed * 0.5f));
                LastHeight = height; LastSpeed = v.magnitude; LastContact = contact;
                if (contact && measure) { _contactLiftSum += height - Mathf.Max(0f, RestHeight); _contactFrames++; }
                // 20.10. (Abstimmung mit Codex nach REP-012): tiefe Ballen, die wegen ihres Tempos NICHT als Kontakt zählen. Beim
                // Loslaufen des Mannes waren das 81 von 84 tiefen Bildern bei 6–8,6 m/s – „0 cm Gleiten“ hieß dort „nicht gemessen“.
                // Obergrenze: enthält auch flache Schwungphasen und Auf-/Abrollen; die Gleit-Werte selbst bleiben unverändert.
                if (!contact && measure && height < low && bodySpeed >= 0.3f) { LowFastFrames++; _lowFastSpeedSum += v.magnitude; }
                if (contact && !_planted) { _planted = true; _plantPos = pos; _episodeDrift = 0f; }
                else if (contact)
                {
                    Vector3 d = pos - _plantPos; d.y = 0f;
                    _episodeDrift = Mathf.Max(_episodeDrift, d.magnitude);
                }
                else if (_planted) End(measure);
            }

            private void End(bool measure)
            {
                _planted = false;
                if (!measure) return;
                Contacts++;
                SumDrift += _episodeDrift;
                MaxDrift = Mathf.Max(MaxDrift, _episodeDrift);
            }

            public void Close() { if (_planted) End(true); }
        }

        private void BindFeet()
        {
            Animator a = ActiveAnimator();
            if (a == null || !a.isHuman) return;
            _lFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rFoot = a.GetBoneTransform(HumanBodyBones.RightFoot);
            _lToe = a.GetBoneTransform(HumanBodyBones.LeftToes);
            _rToe = a.GetBoneTransform(HumanBodyBones.RightToes);
            // 15.10. (3): Fehlt einer Seite die Zehen-Zuordnung im Avatar, mass das Lab dort das Sprunggelenk (~10 cm hoeher)
            // und die andere Seite am Ballen – rechts „0 Kontakte“ im Rennen/Sprint, obwohl der Fuss aufsetzt. Jetzt: fehlende
            // Zehe = erstes Kind des Fusses (Ballengelenk); beide Seiten messen so dasselbe Gelenk.
            if (_lToe == null && _lFoot != null && _lFoot.childCount > 0) _lToe = _lFoot.GetChild(0);
            if (_rToe == null && _rFoot != null && _rFoot.childCount > 0) _rToe = _rFoot.GetChild(0);
            _feetBones = $"{(_lToe != null ? _lToe.name : _lFoot != null ? _lFoot.name : "–")}/{(_rToe != null ? _rToe.name : _rFoot != null ? _rFoot.name : "–")}"
                + (a.GetBoneTransform(HumanBodyBones.LeftToes) == null || a.GetBoneTransform(HumanBodyBones.RightToes) == null ? " (Avatar ohne Zehen-Zuordnung!)" : "");
        }
        private string _feetBones = "";

        // 16.10.: Arme und Rumpf messen (Max: „laufen ist komplett falsch“ – die Arme schwangen nicht, das Lab sah nur Fuesse).
        private Transform _lHand, _rHand, _lUpper, _lLower, _rUpper, _rLower, _hipsB, _headB, _chestB;
        private float _lSwingMin, _lSwingMax, _rSwingMin, _rSwingMax, _lElbMin, _lElbMax, _rElbMin, _rElbMax, _leanSum, _leanMax; private int _leanN;
        private void BindArms()
        {
            Animator a = ActiveAnimator();
            if (a == null || !a.isHuman) return;
            _lHand = a.GetBoneTransform(HumanBodyBones.LeftHand); _rHand = a.GetBoneTransform(HumanBodyBones.RightHand);
            _lUpper = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); _lLower = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _rUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm); _rLower = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hipsB = a.GetBoneTransform(HumanBodyBones.Hips); _headB = a.GetBoneTransform(HumanBodyBones.Head); _chestB = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine);
        }
        private void ResetArms() { _lSwingMin = _rSwingMin = _lElbMin = _rElbMin = 1e6f; _lSwingMax = _rSwingMax = _lElbMax = _rElbMax = -1e6f; _leanSum = _leanMax = 0f; _leanN = 0; }
        private void SampleArms()
        {
            if (_hipsB == null) return;
            Transform m = puppet.transform;
            if (_lHand != null) { float f = Vector3.Dot(_lHand.position - _hipsB.position, m.forward); _lSwingMin = Mathf.Min(_lSwingMin, f); _lSwingMax = Mathf.Max(_lSwingMax, f); }
            if (_rHand != null) { float f = Vector3.Dot(_rHand.position - _hipsB.position, m.forward); _rSwingMin = Mathf.Min(_rSwingMin, f); _rSwingMax = Mathf.Max(_rSwingMax, f); }
            if (_lUpper != null && _lLower != null && _lHand != null) { float e = Vector3.Angle(_lUpper.position - _lLower.position, _lHand.position - _lLower.position); _lElbMin = Mathf.Min(_lElbMin, e); _lElbMax = Mathf.Max(_lElbMax, e); }
            if (_rUpper != null && _rLower != null && _rHand != null) { float e = Vector3.Angle(_rUpper.position - _rLower.position, _rHand.position - _rLower.position); _rElbMin = Mathf.Min(_rElbMin, e); _rElbMax = Mathf.Max(_rElbMax, e); }
            if (_headB != null)
            {
                // Rumpfneigung: Huefte→Kopf gegen die Senkrechte, nach vorn positiv
                Vector3 d = _headB.position - _hipsB.position;
                float lean = Mathf.Atan2(Vector3.Dot(d, m.forward), Vector3.Dot(d, m.up)) * Mathf.Rad2Deg;
                _leanSum += lean; _leanMax = Mathf.Abs(lean) > Mathf.Abs(_leanMax) ? lean : _leanMax; _leanN++;
            }
        }
        private string ArmsReport()
        {
            Animator a = ActiveAnimator();
            string layers = "";
            if (a != null)
                for (int i = 0; i < a.layerCount; i++)
                {
                    var ci = a.GetCurrentAnimatorClipInfo(i);
                    layers += string.Format(CultureInfo.InvariantCulture, "{0}{1}={2:0.00}:{3}", i > 0 ? "," : "", a.GetLayerName(i), a.GetLayerWeight(i), ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "-");
                }
            if (_lSwingMin > 1e5f) return " · Arme: nicht gemessen · Ebenen " + layers;
            return string.Format(CultureInfo.InvariantCulture, " · Handschwung vor/zurueck links {0:0}…{1:0} cm rechts {2:0}…{3:0} cm · Ellbogen links {4:0}–{5:0}° rechts {6:0}–{7:0}° · Rumpf vor Ø {8:0.0}° max {9:0.0}° · Ebenen {10}",
                _lSwingMin * 100f, _lSwingMax * 100f, _rSwingMin * 100f, _rSwingMax * 100f, _lElbMin, _lElbMax, _rElbMin, _rElbMax, _leanN > 0 ? _leanSum / _leanN : 0f, _leanMax, layers);
        }

        // 18.10. (Max: „was mache ich falsch, dass du nicht erkennst was falsch ist … Fehler in den Beinen und der Haltung“):
        // Skelett-Mitschrift je Bild – alle Hauptgelenke im Figurenraum (x rechts, y oben, z vorn, Meter). Ausgewertet ausserhalb
        // von Unity gegen Normwerte (Gangbild nach Perry/Muybridge) und gegen die Quell-Aufnahme: Kniewinkel, Fusswinkel beim
        // Aufsetzen, Spurbreite/Ueberkreuzen, X-Beine, Becken, Rumpf, Kopf, Arme.
        private static readonly HumanBodyBones[] SkelBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        };
        private StringBuilder _skelTrace; private Transform[] _skel;
        private void BeginSkeleton()
        {
            Animator a = ActiveAnimator();
            _skelTrace = null; _skel = null;
            if (a == null || !a.isHuman) return;
            _skel = new Transform[SkelBones.Length + 2];
            var kopf = new StringBuilder("t;tempo");
            for (int i = 0; i < SkelBones.Length; i++) { _skel[i] = a.GetBoneTransform(SkelBones[i]); kopf.Append(';').Append(SkelBones[i]).Append("_x;").Append(SkelBones[i]).Append("_y;").Append(SkelBones[i]).Append("_z"); }
            // Zehenspitzen (Blattknochen unter den Zehen) fuer den Fusswinkel
            Transform lt = _skel[9], rt = _skel[13];
            _skel[SkelBones.Length] = lt != null && lt.childCount > 0 ? lt.GetChild(0) : null;
            _skel[SkelBones.Length + 1] = rt != null && rt.childCount > 0 ? rt.GetChild(0) : null;
            kopf.Append(";LeftToeTip_x;LeftToeTip_y;LeftToeTip_z;RightToeTip_x;RightToeTip_y;RightToeTip_z;boden_l;boden_r;becken;ziel_l;boden_unter_wurzel");
            _skelTrace = kopf.Append('\n');
        }
        private void SampleSkeleton(float t)
        {
            if (_skelTrace == null || _skel == null) return;
            Transform m = puppet.transform;
            _skelTrace.Append(t.ToString("0.000", CultureInfo.InvariantCulture)).Append(';').Append(puppet.HorizontalSpeed.ToString("0.00", CultureInfo.InvariantCulture));
            foreach (Transform b in _skel)
            {
                Vector3 p = b != null ? m.InverseTransformPoint(b.position) : Vector3.zero;
                _skelTrace.Append(';').Append(p.x.ToString("0.0000", CultureInfo.InvariantCulture))
                          .Append(';').Append(p.y.ToString("0.0000", CultureInfo.InvariantCulture))
                          .Append(';').Append(p.z.ToString("0.0000", CultureInfo.InvariantCulture));
            }
            if (_poser == null && _bodies != null && _bodies.ActiveModel != null) _poser = _bodies.ActiveModel.GetComponent<Procedural.CharacterMotionPoser>();
            Vector4 dfu = _poser != null ? _poser.DebugFuss : Vector4.zero;
            float unter = Physics.Raycast(m.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2f, ~(1 << puppet.gameObject.layer), QueryTriggerInteraction.Ignore) ? hit.point.y - m.position.y : 99f;
            _skelTrace.Append(';').Append(string.Join(";", new[] { dfu.x, dfu.y, dfu.z, dfu.w, unter }.Select(v => v.ToString("0.0000", CultureInfo.InvariantCulture))));
            _skelTrace.Append('\n');
        }

        private void SampleFeet(bool measure)
        {
            int mask = ~(1 << puppet.gameObject.layer);
            float body = puppet.HorizontalSpeed;
            Sample(_lTrack, _lToe != null ? _lToe : _lFoot, measure, mask, body);
            Sample(_rTrack, _rToe != null ? _rToe : _rFoot, measure, mask, body);
            if (measure && _feetTrace != null)
            {
                if (_poser == null && _bodies != null && _bodies.ActiveModel != null) _poser = _bodies.ActiveModel.GetComponent<Procedural.CharacterMotionPoser>();
                _feetTrace.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000};{1:0.0};{2:0.00};{3:0.0};{4:0.00};{5};{6:0.0};{7:0.00};{8};{9:0.00};{10:0.00}",
                    _feetT += Time.deltaTime, Time.deltaTime * 1000f, body, _lTrack.LastHeight * 100f, _lTrack.LastSpeed, _lTrack.LastContact ? 1 : 0,
                    _rTrack.LastHeight * 100f, _rTrack.LastSpeed, _rTrack.LastContact ? 1 : 0,
                    _poser != null ? _poser.LocoRate : 0f, _poser != null ? _poser.NativeSpeedEstimate : 0f));
            }
        }

        /// <summary>Fuss-Mitschrift je Abschnitt (15.10. (3)): Hoehe ueber Boden und Tempo beider Ballen je Bild – um
        /// „Kontakte 0“, Gleiten beim Anhalten und „im Boden“ bis zur Ursache zu verfolgen.</summary>
        private StringBuilder _feetTrace; private float _feetT;
        private Procedural.CharacterMotionPoser _poser;

        /// <summary>Ruhehöhe der Ballen im Stand vor dem Abschnitt lernen (Bezug der Kontaktschwelle).</summary>
        private void SampleFeetRest()
        {
            int mask = ~(1 << puppet.gameObject.layer);
            if (_lToe != null || _lFoot != null) _lTrack.SampleRest(Height(_lToe != null ? _lToe : _lFoot, mask, out _));
            if (_rToe != null || _rFoot != null) _rTrack.SampleRest(Height(_rToe != null ? _rToe : _rFoot, mask, out _));
        }

        private readonly RaycastHit[] _hits = new RaycastHit[8];

        /// <summary>
        /// Höhe über dem Boden. 15.10.: Strahl von Hüfthöhe der Figur (nicht 0,5 m über der Zehe) und ohne eigene Collider
        /// (Trefferzonen, Waffe, Schild liegen auf anderen Ebenen als die Figur) – sonst zählt ein Treffer auf dem eigenen
        /// Bein/Schild als „Fuss im Boden“. Der getroffene Collider wird für den Bericht gemerkt.
        /// </summary>
        private float Height(Transform foot, int mask, out string hitName)
        {
            Vector3 p = foot.position;
            float top = Mathf.Max(p.y, puppet.transform.position.y) + 0.5f;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, top, p.z), Vector3.down, _hits, top - p.y + 1.5f, mask, QueryTriggerInteraction.Ignore);
            float ground = p.y - 0.3f, best = float.MaxValue;
            hitName = "–";
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(puppet.transform)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; ground = _hits[i].point.y; hitName = c.name; }
            }
            return p.y - ground;
        }

        private void Sample(FootTrack track, Transform foot, bool measure, int mask, float bodySpeed)
        {
            if (foot == null) return;
            float h = Height(foot, mask, out string hit);
            track.Sample(foot.position, h, measure, Time.deltaTime, bodySpeed, hit);
        }

        /// <summary>SSAO-Renderer-Funktion des aktiven URP-Renderers an/aus (nur Lab-Gegenprobe; am Ende und beim Beenden wieder an).</summary>
        private static bool _aoAus;
        private static void Ao(bool an)
        {
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp == null) return;
                var fld = rp.GetType().GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (!(fld?.GetValue(rp) is Array liste)) return;
                foreach (var data in liste)
                {
                    if (data == null) continue;
                    var feats = data.GetType().GetProperty("rendererFeatures")?.GetValue(data) as System.Collections.IList;
                    if (feats == null) continue;
                    foreach (var f in feats)
                        if (f is ScriptableObject so && so.name.Contains("AmbientOcclusion"))
                            f.GetType().GetMethod("SetActive")?.Invoke(f, new object[] { an });
                }
                _aoAus = !an;
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Lab SSAO-Schalter: " + e.Message); }
        }

        private void OnDisable() { if (_aoAus) Ao(true); Time.captureDeltaTime = 0f; }   // 22.10.: auch bei Abbruch zurück

        private AnimatorVerzugMessung _verzug;

        /// <summary>20.10. (REQ-010 Block A): Messung je Abschnitt starten (true) bzw. Berichtsteil liefern (false).</summary>
        private string Verzug(bool start)
        {
            if (start)
            {
                if (_verzug == null) _verzug = gameObject.GetComponent<AnimatorVerzugMessung>() ?? gameObject.AddComponent<AnimatorVerzugMessung>();
                _verzug.Motor = puppet; _verzug.Animator = ActiveAnimator(); _verzug.Reset0(); _verzug.Aktiv = true;
                return "";
            }
            if (_verzug == null || _verzug.Bilder == 0) return "";
            _verzug.Aktiv = false;
            return string.Format(CultureInfo.InvariantCulture, " · Animator-Verzug: Zustand falsch {0}/{1} Bilder, VerticalSpeed daneben Ø {2:0.000} max {3:0.00} m/s",
                _verzug.BoolFalsch, _verzug.Bilder, _verzug.VySumme / _verzug.Bilder, _verzug.VyMax);
        }

        private Animator ActiveAnimator()
        {
            GameObject m = _bodies != null ? _bodies.ActiveModel : null;
            return m != null ? m.GetComponent<Animator>() : puppet.GetComponentInChildren<Animator>();
        }

        /// <summary>Detailstufe des Körpers erzwingen (−1 = wieder automatisch).</summary>
        private void StufeErzwingen(int stufe)
        {
            Animator fa = ActiveAnimator();
            LODGroup lg = fa != null ? fa.transform.root.GetComponentInChildren<LODGroup>() : null;
            if (lg != null) lg.ForceLOD(stufe);
        }

        // ================================================================== Entwickler-Panel (§107–§109)

        private static readonly float[] TimeScales = { 0.1f, 0.25f, 0.5f, 1f };
        private static readonly string[] Weapons = { "Schwert+Schild", "Schwert", "Pistole", "Unbewaffnet" };
        private static readonly WeaponFamily[] WeaponValues = { WeaponFamily.SwordShield, WeaponFamily.OneHandSword, WeaponFamily.Pistol, WeaponFamily.Unarmed };
        private int _weapon, _armor = 3, _role;
        private bool _female;

        private void Update()
        {
#if ASHVEIL_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) _panel = !_panel;
#endif
        }

        private void OnGUI()
        {
            if (_recording)
            {
                GUI.Label(new Rect(10, 10, 600, 24), "Motion-Lab Aufnahme: " + _status);
                return;
            }
            if (!_panel || _orchestrator == null) return;
            GUILayout.BeginArea(new Rect(10, 10, 330, 520), GUI.skin.box);
            GUILayout.Label("Motion-Lab (F8 aus/ein)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Zeit", GUILayout.Width(60));
            foreach (float ts in TimeScales)
                if (GUILayout.Toggle(Mathf.Approximately(Time.timeScale, ts), ts.ToString("0.##", CultureInfo.InvariantCulture) + "x", GUI.skin.button)) Time.timeScale = ts;
            GUILayout.EndHorizontal();

            Procedural.CharacterMotionPoser.GlobalEnabled = GUILayout.Toggle(Procedural.CharacterMotionPoser.GlobalEnabled,
                Procedural.CharacterMotionPoser.GlobalEnabled ? "Prozedurale Schicht: AN" : "Prozedurale Schicht: AUS (roh)", GUI.skin.button);
            bool female = GUILayout.Toggle(_female, _female ? "Koerper: weiblich" : "Koerper: maennlich", GUI.skin.button);
            int role = GUILayout.Toolbar(_role, new[] { "Tank", "Schaden", "Heiler" });
            int armor = GUILayout.Toolbar(_armor - 1, new[] { "Leicht", "Mittel", "Schwer" }) + 1;
            int weapon = GUILayout.SelectionGrid(_weapon, Weapons, 2);
            if (female != _female || role != _role || armor != _armor || weapon != _weapon)
            {
                _female = female; _role = role; _armor = armor; _weapon = weapon;
                if (_bodies != null) _bodies.Apply(_female ? 1 : 0);
                _orchestrator.SetOverride(new CharacterMotionOrchestrator.IdentityOverride
                {
                    Active = true, RaceId = RaceCatalog.HumanId,
                    Body = _female ? BodyMotionVariant.Feminine : BodyMotionVariant.Masculine,
                    Role = _role == 0 ? ClassRole.Tank : _role == 1 ? ClassRole.Damage : ClassRole.Healer,
                    Armor = (ArmorCategory)_armor, Weapon = WeaponValues[_weapon]
                });
            }

            MotionContext c = _orchestrator.Context;
            ResolvedMotionTuning tu = _orchestrator.Tuning;
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "Haltung {0} · Gang {1}\nTempo {2:0.0} m/s · Beschl. {3:0.0} · Drehung {4:0}°/s\nBoden {5} · Ausdauer {6:0}% · {7}\nZiel {8} {9:0.0} m · {10:0}°\nSchichten: {11}/{12}/{13}/{14}/{15}\nArmschwung {16:0.00} · Masse {17:0.00} · Oberkoerper {18:0.00}\nLetzter Angriff: {19}",
                c.Stance, c.Gait, c.Speed, c.Acceleration, c.TurnRate, c.Grounded ? "ja" : "nein", c.StaminaNormalized * 100f, c.Wounded,
                c.HasTarget ? c.TargetSize.ToString() : "-", c.TargetDistance, c.TargetAngle,
                tu.body != null ? tu.body.variant.ToString() : "-", tu.race != null ? tu.race.raceId : "-", tu.classMotion != null ? tu.classMotion.role.ToString() : "-",
                tu.armor != null ? tu.armor.category.ToString() : "-", tu.weapon != null ? tu.weapon.family.ToString() : "-",
                tu.modifiers.armSwingScale, tu.modifiers.massInertia, tu.modifiers.upperBodyMotion,
                _orchestrator.LastAttack.Valid ? _orchestrator.LastAttack.Definition.label : "-"));
            GUILayout.EndArea();
        }
    }
}
