using System;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Kompletter Simulationszustand des Bewegungsmotors.
    /// Alles, was das Ergebnis des naechsten Schritts beeinflusst, steht hier drin.
    /// Das ist die Voraussetzung dafuer, dass der Server spaeter einen Zustand
    /// korrigieren und der Client ab dort neu simulieren kann (Reconciliation).
    /// </summary>
    [Serializable]
    public struct MotorState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;

        public bool Grounded;
        public float CoyoteTimer;
        public float JumpBufferTimer;
        public float AirTime;
        public float PeakFallSpeed;
        public float LandingRecovery;

        public float Stamina;
        public float StaminaRegenDelay;
        public bool Exhausted;
        public bool IsSprinting;
        public bool IsWalking;
        public bool IsJogging;
        public bool IsEpicWalking;

        // Ausweichen
        public float DodgeTimer;
        public float DodgeCooldown;
        public Vector3 DodgeDirection;

        // Sturzangriff
        public bool IsPlunging;

        // Praxis-Meisterschaften: Reiserhythmus 0..1 und die Wirkungen, die der Server setzt. Sie reisen mit dem
        // Abgleich zum Besitzer – Vorhersage und Server rechnen so immer mit denselben Werten.
        public float TravelMomentum;
        public bool InCombat;
        public float TravelBonus;
        public float SprintSaving;
        public float DodgeSaving;
        public float DodgeRecovery;

        // Klassenreittier (Eidwaechter): aufgesessen + Tempo-Aufschlag inkl. Bindung. Setzt der Server, reist mit dem Abgleich.
        public bool Mounted;
        public float MountSpeed;

        // P1 Klassenfaehigkeiten: Ansturm/Rueckzug – der Server setzt einen Schub, der mit dem Abgleich zum Besitzer reist
        public float DashTimer;
        public Vector3 DashVelocity;
        public float DashLift;

        // Schwimmen und Tauchen (MOV-169, Welle 0): im Wasser, Kopf unter Wasser, Atem in Sekunden, Ertrinken. Teil des
        // Abgleichs – Vorhersage und Server rechnen mit denselben Werten; den Ertrinkungsschaden verteilt der Server.
        public bool Swimming;
        public bool Underwater;
        public float Breath;
        public bool Drowning;
    }
}
