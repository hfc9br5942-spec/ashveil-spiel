using System;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Tuning-Werte des Bewegungsmotors. Werte sind bewusst "MMO-knackig":
    /// schnelle Beschleunigung, Sprung behaelt Schwung, rueckwaerts langsamer.
    /// </summary>
    [Serializable]
    public sealed class CharacterMotorSettings
    {
        /// <summary>
        /// Stand der Standardwerte. Aeltere gespeicherte Einstellungen (Prefabs/Szenen) werden beim Start auf die
        /// neuen Werte gehoben (<see cref="Migrate"/>), ohne dass jedes Prefab neu gebaut werden muss.
        /// 2: Bewegung wie WoW Classic (29.09.2026, Max) – umgerechnet auf die Koerpergroesse (siehe Migrate).
        /// 3: Atem unter Wasser 90 s statt 60 s (Max 06.10.2026: „Mehr Luft (90 s)“, sonst wie Classic).
        /// 4: Gehen 1,5 statt 2,25 m/s, Joggen 3,0 statt 4,0 (Max 18.10.2026, Klick „Gehen langsamer“: echte Bewegungsaufnahmen
        ///    passen nur zu menschlichen Tempi; Rennen 6,3 und Sprint 9 bleiben wie Classic).
        /// 5: zurück auf Gehen 2,25 und Joggen 4,0 (Max 18.10.2026 nachts, Klick „Zurück auf alt“: Bewegung bleibt vorerst UAL,
        ///    die gesenkten Tempi aus Stand 4 waren nur für echte Aufnahmen nötig).
        /// </summary>
        public const int CurrentVersion = 5;
        [HideInInspector] public int version; // bewusst ohne Startwert: fehlt das Feld in alten Daten, ist es 0 → Migrate hebt an

        [Header("Geschwindigkeiten (m/s) – WoW Classic, auf 1,8-m-Figuren umgerechnet")]
        public float runSpeed = 6.3f;
        [Tooltip("Vorlaeufig: Sprint wird spaeter eine Faehigkeit mit Abklingzeit")]
        public float sprintSpeed = 9.0f;
        public float walkSpeed = 2.25f;
        [Tooltip("Joggen – zwischen Gehen und Rennen (eigene Gangart, Umschalttaste Num *)")]
        public float jogSpeed = 4.0f;
        [Tooltip("Epischer Gang – ruhig, steuerbar (Umschalttaste Num -)")]
        public float epicWalkSpeed = 1.6f;
        public float backwardSpeed = 4.05f;
        [Tooltip("Grad pro Sekunde beim Drehen mit A/D")]
        public float turnSpeed = 180f;

        [Header("Beschleunigung (m/s^2)")]
        public float groundAcceleration = 60f;
        public float groundDeceleration = 45f;
        [Tooltip("Luftsteuerung: klein, damit Spruenge Schwung behalten")]
        public float airAcceleration = 9f;

        [Header("Springen & Fallen – WoW Classic (Absprung 7,96 yd/s, Schwerkraft 19,29 yd/s²), umgerechnet")]
        public float jumpHeight = 1.48f;
        public float gravity = 17.36f;
        public float maxFallSpeed = 54f;
        [Tooltip("Kurzes Zeitfenster, in dem man nach dem Verlassen einer Kante noch springen kann")]
        public float coyoteTime = 0.1f;
        [Tooltip("Sprung wird gemerkt, wenn man ihn kurz vor der Landung drueckt")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("Haelt den Charakter beim Bergablaufen am Boden")]
        public float groundSnapDistance = 0.45f;
        public float groundStickVelocity = 2f;
        [Tooltip("Maximale Rutschgeschwindigkeit an zu steilen Haengen")]
        public float maxSlideSpeed = 9f;

        [Header("Harte Landung")]
        [Tooltip("Ab dieser Aufprallgeschwindigkeit gilt eine Landung als hart")]
        public float hardLandingSpeed = 14f;
        public float hardLandingRecovery = 0.35f;
        [Range(0f, 1f)] public float hardLandingSpeedMultiplier = 0.35f;

        [Header("Ausdauer (Sprint)")]
        public float maxStamina = 100f;
        public float sprintStaminaPerSecond = 16f;
        public float staminaRegenPerSecond = 14f;
        public float staminaRegenDelay = 1.0f;
        [Tooltip("Nach Erschoepfung erst ab diesem Wert wieder sprinten")]
        public float staminaRecoverThreshold = 25f;

        [Header("Hechtrolle (Taste 4) – schnell Distanz ueberbruecken, KEINE Unverwundbarkeit")]
        [Tooltip("Zurueckgelegte Strecke in m")]
        public float dodgeDistance = 7.5f;
        [Tooltip("Dauer in s – kurz, damit man im letzten Moment noch aus einem Angriff hechten kann")]
        public float dodgeDuration = 0.38f;
        public float dodgeCooldown = 4f;
        public float dodgeStaminaCost = 25f;
        [Tooltip("Anteil der Strecke, der in der ersten Haelfte zurueckgelegt wird (0,5 = gleichmaessig)")]
        [Range(0.5f, 0.9f)] public float dodgeFrontLoad = 0.72f;

        [Header("Reiserhythmus (Praxis-Meisterschaften §14/§15)")]
        [Tooltip("Sekunden durchgehenden Laufens ausserhalb des Kampfes bis zum vollen Reiserhythmus")]
        public float travelBuildSeconds = 6f;
        [Tooltip("Sekunden, in denen der Rhythmus beim Stehenbleiben verfliegt")]
        public float travelFadeSeconds = 2f;

        [Header("Reittier (Eidwaechter)")]
        [Tooltip("Beschleunigung im Sattel – ein schweres Tier nimmt langsamer Fahrt auf")]
        public float mountedAcceleration = 22f;
        public float mountedDeceleration = 26f;
        [Tooltip("Schritt- und Rueckwaertstempo im Sattel (Faktor auf Gehen bzw. Rueckwaerts)")]
        public float mountedWalkFactor = 1.4f;
        public float mountedBackFactor = 1.1f;
        [Tooltip("Drehen mit A/D im Sattel (Grad/s) – etwas traeger als zu Fuss")]
        public float mountedTurnSpeed = 150f;

        [Header("Sturzangriff")]
        public float plungeSpeed = 28f;

        [Header("Schwimmen und Tauchen (MOV-169, Welle 0) – WoW Classic, auf 1,8-m-Figuren umgerechnet; Werte offen (11 §Y)")]
        [Tooltip("Ab dieser Wassertiefe (Spiegel über den Füßen, m) schwimmt man – etwa Brusthöhe")]
        public float swimEnterDepth = 1.3f;
        [Tooltip("Erst unter dieser Tiefe geht man wieder (Hysterese an der Uferkante)")]
        public float swimExitDepth = 1.1f;
        [Tooltip("Schwimmhöhe: Füße so weit unter dem Spiegel – Kopf und Schultern bleiben über Wasser")]
        public float swimFloatDepth = 1.35f;
        [Tooltip("Augenhöhe über den Füßen: liegt sie unter dem Spiegel, läuft die Atemleiste ab")]
        public float swimHeadHeight = 1.6f;
        [Tooltip("Schwimmen vorwärts (Classic 4,72 yd/s × 0,9)")]
        public float swimSpeed = 4.25f;
        [Tooltip("Schwimmen rückwärts (Classic 2,5 yd/s × 0,9)")]
        public float swimBackSpeed = 2.25f;
        public float swimAcceleration = 14f;
        public float swimDeceleration = 10f;
        [Tooltip("Auf- und Absteigen mit Leertaste / X (m/s)")]
        public float swimVerticalSpeed = 3f;
        [Tooltip("Wasserwiderstand senkrecht: ein Sturz ins tiefe Wasser ist in ≈ 0,3 s abgebremst")]
        public float swimVerticalAcceleration = 60f;
        [Tooltip("Tote Zone der Kamera-Neigung beim Tauchen (Grad): dazwischen schwimmt man geradeaus")]
        public float diveDeadLow = 0f;
        public float diveDeadHigh = 25f;

        [Header("Atem und Ertrinken (MOV-169) – Max 06.10.: 90 s Luft, sonst wie Classic")]
        [Tooltip("Atemvorrat unter Wasser in Sekunden (Max 06.10.: 90 s; Classic: 60 s)")]
        public float breathSeconds = 90f;
        [Tooltip("Auffüllen an der Luft (Sekunden Atem je Sekunde)")]
        public float breathRefillPerSecond = 10f;
        [Tooltip("Ertrinken: Abstand der Schadenstakte in Sekunden")]
        public float drownInterval = 2f;
        [Tooltip("Ertrinken: Anteil des Höchstlebens je Takt")]
        [Range(0f, 1f)] public float drownFraction = 0.1f;

        public float JumpVelocity => Mathf.Sqrt(2f * gravity * jumpHeight);

        /// <summary>
        /// Aeltere Werte auf den aktuellen Stand heben. WoW Classic in Welt-Einheiten: Rennen 7, Gehen 2,5, Rueckwaerts 4,5,
        /// Absprung 7,96, Schwerkraft 19,29, Endgeschwindigkeit 60,1. Eine WoW-Menschenfigur ist rund 2 Einheiten gross,
        /// unsere 1,8 m – deshalb alles × 0,9, damit es sich im Verhaeltnis zum Koerper genauso anfuehlt.
        /// </summary>
        public void Migrate()
        {
            if (version >= CurrentVersion) return;
            if (version < 2)
            {
                runSpeed = 6.3f; walkSpeed = 2.25f; backwardSpeed = 4.05f; sprintSpeed = 9.0f;
                jogSpeed = 4.0f; epicWalkSpeed = 1.6f;
                jumpHeight = 1.48f; gravity = 17.36f; maxFallSpeed = 54f;
            }
            // 3: gespeicherte 60 s (alter Standard) werden 90 s; von Hand eingestellte Werte bleiben
            if (version < 3 && Mathf.Approximately(breathSeconds, 60f)) breathSeconds = 90f;
            // 5: Stand 4 wieder zurücknehmen (nur die damaligen Standardwerte)
            if (version == 4)
            {
                if (Mathf.Approximately(walkSpeed, 1.5f)) walkSpeed = 2.25f;
                if (Mathf.Approximately(jogSpeed, 3.0f)) jogSpeed = 4.0f;
            }
            version = CurrentVersion;
        }
    }
}
