using System;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Ein Bewegungsbefehl fuer einen einzelnen Simulationsschritt.
    ///
    /// Das ist die einzige Information, die spaeter vom Client zum Server geschickt wird.
    /// Der Server simuliert mit exakt diesem Befehl denselben Schritt wie der Client
    /// (Client-Prediction + Server-Reconciliation). Deshalb enthaelt er nur Absichten,
    /// niemals Positionen oder Geschwindigkeiten.
    /// </summary>
    [Serializable]
    public struct MoveCommand
    {
        /// <summary>-1 rueckwaerts .. +1 vorwaerts</summary>
        public float Forward;
        /// <summary>-1 links .. +1 rechts</summary>
        public float Strafe;
        /// <summary>Tastatur-Drehen: -1 links .. +1 rechts (nur ohne Maussteuerung)</summary>
        public float Turn;
        /// <summary>true = Charakter uebernimmt <see cref="DesiredYaw"/> (rechte Maustaste).</summary>
        public bool SteerWithCamera;
        /// <summary>Blickrichtung in Grad (Kamera-Yaw), nur gueltig mit SteerWithCamera.</summary>
        public float DesiredYaw;
        /// <summary>Sprungtaste wurde in diesem Frame gedrueckt (Flanke, nicht gehalten).</summary>
        public bool Jump;
        public bool Sprint;
        public bool Walk;
        /// <summary>Joggen (Gangart zwischen Gehen und Rennen).</summary>
        public bool Jog;
        /// <summary>Epischer Gang – ruhig und steuerbar, kein Emote.</summary>
        public bool EpicWalk;
        /// <summary>Ausweichrolle ausloesen (Flanke). Richtung = Bewegungseingabe, ohne Eingabe rueckwaerts.</summary>
        public bool Dodge;
        /// <summary>Sturzangriff: in der Luft schnell nach unten stossen.</summary>
        public bool Plunge;
        /// <summary>Schwimmen (MOV-169): aufsteigen, solange die Sprungtaste gehalten wird.</summary>
        public bool SwimUp;
        /// <summary>Schwimmen (MOV-169): absinken, solange X gehalten wird.</summary>
        public bool SwimDown;
        /// <summary>Kamera-Neigung in Grad (positiv = nach unten). Mit Kamerasteuerung bestimmt sie im Wasser den Tauchwinkel.</summary>
        public float SwimPitch;

        public bool HasMovementInput => Mathf.Abs(Forward) > 0.01f || Mathf.Abs(Strafe) > 0.01f;
    }
}
