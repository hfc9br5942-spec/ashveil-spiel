using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Grenzgebirge (Stufe 16–22): erste Gegner, damit Wege, Tempo und das Streaming mit Gegnern je Zelle pruefbar sind.
    /// PLATZHALTER im Inhalt: Koerper und Faehigkeiten der Kareth-Gegner, Beute aus vorhandenen Tabellen – eigene Arten,
    /// Beute und Auftraege fuer 16–22 folgen im Inhalts-Pass. Namen passen zum Design (Gebirge, Deserteure an der Grenze).
    /// Dichte bewusst massvoll und abseits der Hauptwege: wer nur durchreist, wird nicht alle 20 m aufgehalten.
    /// </summary>
    public static partial class EnemyBuilder
    {
        public sealed class BorderEnemy
        {
            public EnemyDefinition Def;
            public GameObject Prefab;
        }

        public const string Gratwolf = "enemy.ridge_wolf", Deserteur = "enemy.deserter", DeserteurSchuetzin = "enemy.deserter_gunner",
            Spaltenspeier = "enemy.cleft_spitter", Felsschrat = "enemy.rock_brute";

        /// <summary>Definitionen und Prefabs der Grenzgebirge-Gegner (idempotent). Schluessel = Gegner-ID.</summary>
        public static Dictionary<string, BorderEnemy> BuildBorderEnemies()
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();
            var r = new Dictionary<string, BorderEnemy>();

            EnemyDefinition wolf = EnsureDefinition("Enemy_Gratwolf", Gratwolf, "Gratwolf", d =>
            {
                SetupWolf(d);
                d.minLevel = 16; d.maxLevel = 18;
                d.aggroRadius = 14f; d.leashDistance = 45f;
                d.bodyColor = new Color(0.36f, 0.37f, 0.4f); d.glowColor = new Color(0.8f, 0.85f, 1f); d.scale = 1.12f;
                d.respawnTime = 60f;
            });
            EnemyDefinition deserter = EnsureDefinition("Enemy_Deserteur", Deserteur, "Deserteur", d =>
            {
                SetupHusk(d);
                d.minLevel = 18; d.maxLevel = 20;
                d.walkSpeed = 1.4f; d.runSpeed = 5.6f;
                d.bodyColor = new Color(0.38f, 0.3f, 0.24f); d.glowColor = new Color(1f, 0.7f, 0.4f);
                d.fleeHealthFraction = 0.12f; // Deserteure laufen, wenn es eng wird
                d.respawnTime = 70f;
            });
            EnemyDefinition gunner = EnsureDefinition("Enemy_DeserteurSchuetzin", DeserteurSchuetzin, "Deserteurin", d =>
            {
                SetupGunner(d);
                d.minLevel = 18; d.maxLevel = 20;
                d.bodyColor = new Color(0.38f, 0.3f, 0.24f); d.glowColor = new Color(1f, 0.7f, 0.4f);
                d.respawnTime = 70f;
            });
            EnemyDefinition spitter = EnsureDefinition("Enemy_Spaltenspeier", Spaltenspeier, "Spaltenspeier", d =>
            {
                SetupNeedleSpitter(d);
                d.minLevel = 16; d.maxLevel = 17;
                d.bodyColor = new Color(0.34f, 0.33f, 0.36f); d.glowColor = new Color(0.6f, 0.9f, 1f);
                d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 8f; // an der Hauptroute nicht aggressiv
            });
            EnemyDefinition brute = EnsureDefinition("Enemy_Felsschrat", Felsschrat, "Felsschrat", d =>
            {
                SetupPeatBrute(d);
                d.minLevel = 21; d.maxLevel = 22;
                d.baseHealth = 520f; d.healthPerLevel = 45f; // Elite am Hochpass (Route A: staerkere Gegner)
                d.bodyColor = new Color(0.4f, 0.39f, 0.37f); d.glowColor = new Color(0.9f, 0.8f, 0.6f); d.scale = 1.25f;
                d.fireDamageTaken = 1f; d.respawnTime = 300f;
            });

            r[Gratwolf] = new BorderEnemy { Def = wolf, Prefab = BuildBeastPrefab(wolf) };
            // #28 (07.10.): Deserteure sind Menschen – Menschen-Platzhalter statt Asche-Haut und Glutaugen (wie ApplyDeserterLook)
            CreateKarethMaterials();
            r[Deserteur] = new BorderEnemy
                { Def = deserter, Prefab = Tint(BuildHumanoidPrefab(deserter, false), (_ashSkin, _kDeserterCloth), (_ember, _kDeserterEye)) };
            r[DeserteurSchuetzin] = new BorderEnemy
                { Def = gunner, Prefab = Tint(BuildHumanoidPrefab(gunner, true), (_ashSkin, _kDeserterCloth), (_ember, _kDeserterEye)) };
            r[Spaltenspeier] = new BorderEnemy { Def = spitter, Prefab = BuildNeedleSpitterPrefab(spitter) };
            r[Felsschrat] = new BorderEnemy { Def = brute, Prefab = BuildColossusPrefab(brute, false) };
            ItemContentBuilder.AssignBorderLoot();
            return r;
        }

        /// <summary>Spawngebiet an einer Weltstelle (auch in Zellen-Szenen; ohne NetworkObject).</summary>
        public static EnemySpawnArea PlaceArea(Transform parent, string name, Vector3 pos, BorderEnemy e, int count, float radius, string group,
            int minLevel = 0, int maxLevel = 0) => Area(parent, name, pos, e.Def, e.Prefab, count, radius, group, minLevel, maxLevel);
    }
}
