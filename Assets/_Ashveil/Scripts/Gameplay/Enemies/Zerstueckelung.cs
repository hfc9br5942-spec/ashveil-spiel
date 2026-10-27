using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// 22.10.2026 (Max: „Zerstückelung von kleineren Mobs und Gegnern soll die Todesanimation werden“, nur beim kritischen
    /// Todesstoß – Zeichen deutlicher Überlegenheit; abgetrennte Teile bleiben eine Weile liegen; COMBAT-026a/b):
    /// Glied eines verformbaren Gegners (Humanoid-Skelett) an einem festen Trennpunkt abtrennen – nur Darstellung, jeder Rechner
    /// für sich. OB und WO getrennt wird, entscheidet der Server einmal am tatsächlichen Treffer (<see cref="TrennpunktFuer"/>,
    /// Codex-Review 22.10.) und schickt es mit dem Treffer (DamageInfo.Trennpunkt, NetworkHealth.ObserversDamaged) – so trennen
    /// Host und alle Clients dasselbe Glied, unabhängig von der lokalen Pose und der Waffe, die der Angreifer gerade hält. Kein freies Netz-Schneiden: die Dreiecke des Gliedes (stärkster Knochen in der Gliedkette) werden
    /// am Körper ausgeblendet und als gebackenes, starres Teil mit Physik abgelöst; Stumpf und Schnittfläche bekommen eine
    /// Fleischkappe, dazu Blutspritzer und Fleck (EnemyGore, Einstellung „Blut und Zerteilung“ = Voll).
    /// </summary>
    public static class Zerstueckelung
    {
        public enum Trennpunkt : byte { Keiner, Kopf, OberarmL, OberarmR, UnterarmL, UnterarmR, OberschenkelL, OberschenkelR }

        /// <summary>Höchstens so viele lose Teile in der Welt; das älteste verschwindet zuerst.</summary>
        public const int MaxTeile = 24;
        /// <summary>Liegezeit (s) – länger als die Leiche ist egal, die Teile hängen unter der Leiche und gehen mit ihr.</summary>
        public const float Liegezeit = 180f;

        // Lose Teile aller Gegnerarten (Mensch und Tier) – ein gemeinsames Budget MaxTeile/Liegezeit, ältestes zuerst
        private static readonly List<Liegt> Liegend = new List<Liegt>();
        private static readonly Color BlutFarbe = new Color(0.32f, 0.02f, 0.02f), FleischFarbe = new Color(0.55f, 0.12f, 0.1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Liegend.Clear();

        /// <summary>Chance je Berechtigtem, dass das abgetrennte Teil Zusatzbeute enthält (COMBAT-026b: „mit geringer Chance“).</summary>
        public const float ZusatzChance = 0.10f;

        /// <summary>
        /// 23.10.2026 (Max, COMBAT-026b: „abgetrennte teile … können mit geringer chance zusatzloot enthalten“): Server, nach der normalen
        /// Leichenbeute. Wurde der Gegner beim Todesstoß zerstückelt, würfelt jeder Berechtigte mit <see cref="ZusatzChance"/> einen weiteren
        /// Wurf aus der Beutetabelle des Gegners; der erste Gegenstand kommt als Zusatz in seine persönliche Leichenbeute. Bewusst kein eigenes
        /// Netzobjekt am Teil (Abstimmung Spielablauf-Sitzung): so gelten Glimmen, Beutefenster, persönliche Beute, Auftragsbeute-Regel und
        /// Wiederverbinden ohne Zusatzarbeit. [PROVISIONAL] Abweichung vom Kanontext „eigenes, plünderbares Teil“ – Max kann es umstellen.
        /// Rückgabe: Anzahl der Berechtigten mit Zusatzbeute.
        /// </summary>
        public static int ZusatzBeute(byte trennpunkt, Ashveil.Gameplay.Items.CorpseLoot leiche, Ashveil.Data.LootTableDefinition tabelle,
            IEnumerable<string> berechtigte, System.Func<string, Ashveil.Data.CharacterClassDefinition> klasseVon, int seed,
            System.Func<string, Ashveil.Data.ItemDefinition, bool> erlaubt = null, float chance = ZusatzChance)
        {
            if (trennpunkt == 0 || leiche == null || tabelle == null || berechtigte == null) return 0;
            int n = 0, i = 0;
            foreach (string p in berechtigte)
            {
                if (string.IsNullOrEmpty(p)) continue;
                var rng = new System.Random(unchecked(seed * 92821 + (i++ + 1) * 68917 + p.GetHashCode()));
                if (rng.NextDouble() >= chance) continue;
                string wer = p;
                List<Ashveil.Data.ItemStack> wurf = Ashveil.Gameplay.Items.LootGenerator.Roll(tabelle, klasseVon != null ? klasseVon(p) : null, rng,
                    erlaubt != null ? item => erlaubt(wer, item) : (System.Func<Ashveil.Data.ItemDefinition, bool>)null);
                if (wurf == null || wurf.Count == 0) continue;
                leiche.AddBonus(new[] { p }, wurf[0].id, wurf[0].count);
                n++;
            }
            return n;
        }

        /// <summary>Lose Teile, die gerade im Budget liegen (Prüfungen).</summary>
        public static int LiegendeTeile => Liegend.Count;

        /// <summary>
        /// Server (und Einzelspiel): darf dieser Treffer abtrennen, und wo? Nur tödlich (<paramref name="neuesLeben"/> ≤ 0),
        /// kritisch und mit einer Hiebwaffe laut <see cref="Combat.DamageInfo.WeaponId"/> des Treffers selbst – Zauber, Gift, Umwelt
        /// und Gegnerangriffe haben keine Waffen-Id einer Hiebwaffe und trennen nie. Den Ort wählt die Darstellung des Ziels aus
        /// Trefferpunkt und Richtung (0 = keiner, z. B. Kolosse). Rein: keine Seiteneffekte, gleiche Eingabe – gleiches Ergebnis.
        /// </summary>
        public static byte TrennpunktFuer(in Combat.DamageInfo info, float neuesLeben, GameObject ziel)
        {
            if (ziel == null || neuesLeben > 0f || !info.IsCritical || info.Dodged || info.Amount <= 0f) return 0;
            if (!TrenntWaffe(info.WeaponId)) return 0;
            EnemyVisual v = ziel.GetComponentInChildren<EnemyVisual>();
            return v != null ? v.TrennpunktFuer(info.HitPoint, info.Direction) : (byte)0;
        }

        /// <summary>
        /// Trennt diese Waffe? Hiebwaffen (Schwert, Axt, je auch zweihändig) ja; Stichwaffen (Dolch,
        /// Speer), Wucht (Hammer, Streitkolben, Stab), Bögen, Feuerwaffen, Zauber und Fäuste nicht – ein Stich oder Pfeil trennt kein
        /// Glied ab, Wucht zertrümmert (COMBAT-026a). Unbekannte Ids (Zauber, Gift, Gegner) trennen nicht.
        /// </summary>
        public static bool TrenntWaffe(string weaponId)
        {
            Ashveil.Data.WeaponDefinition w = Ashveil.Data.GameData.Get<Ashveil.Data.WeaponDefinition>(weaponId);
            return w != null && TrenntKlasse(w.weaponClass);
        }

        public static bool TrenntKlasse(Ashveil.Data.WeaponClass k)
        {
            switch (k)
            {
                case Ashveil.Data.WeaponClass.Sword: case Ashveil.Data.WeaponClass.TwoHandedSword: case Ashveil.Data.WeaponClass.Axe:
                case Ashveil.Data.WeaponClass.TwoHandedAxe:
                    return true;
                default: return false;
            }
        }

        /// <summary>Trennpunkt aus Trefferpunkt und Schlagrichtung: Kopf/Hals, Arm der getroffenen Seite (Ober- oder Unterarm), Bein.</summary>
        public static Trennpunkt Waehle(Animator anim, Vector3 trefferpunkt, Vector3 richtung)
        {
            if (anim == null || !anim.isHuman) return Trennpunkt.Keiner;
            Transform hals = anim.GetBoneTransform(HumanBodyBones.Neck), huefte = anim.GetBoneTransform(HumanBodyBones.Hips);
            Transform ellL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm), ellR = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (hals == null || huefte == null) return Trennpunkt.Keiner;
            Transform root = anim.transform;
            Vector3 p = root.InverseTransformPoint(trefferpunkt);
            float yHals = root.InverseTransformPoint(hals.position).y, yHuefte = root.InverseTransformPoint(huefte.position).y;
            if (p.y >= yHals - 0.06f) return Trennpunkt.Kopf;
            bool links = p.x < 0f;
            if (p.y > yHuefte + 0.12f)
            {
                Transform ell = links ? ellL : ellR;
                bool unter = ell != null && p.y < root.InverseTransformPoint(ell.position).y;
                return links ? (unter ? Trennpunkt.UnterarmL : Trennpunkt.OberarmL) : (unter ? Trennpunkt.UnterarmR : Trennpunkt.OberarmR);
            }
            return links ? Trennpunkt.OberschenkelL : Trennpunkt.OberschenkelR;
        }

        private static HumanBodyBones Knochen(Trennpunkt t)
        {
            switch (t)
            {
                case Trennpunkt.Kopf: return HumanBodyBones.Neck;
                case Trennpunkt.OberarmL: return HumanBodyBones.LeftUpperArm;
                case Trennpunkt.OberarmR: return HumanBodyBones.RightUpperArm;
                case Trennpunkt.UnterarmL: return HumanBodyBones.LeftLowerArm;
                case Trennpunkt.UnterarmR: return HumanBodyBones.RightLowerArm;
                case Trennpunkt.OberschenkelL: return HumanBodyBones.LeftUpperLeg;
                default: return HumanBodyBones.RightUpperLeg;
            }
        }

        /// <summary>
        /// Glied abtrennen. <paramref name="halter"/> = Wurzel der Leiche (Teile hängen darunter und verschwinden mit ihr).
        /// false = nichts getrennt (kein passendes Netz, Einstellung nicht „Voll“).
        /// </summary>
        public static bool Ausfuehren(Animator anim, Trennpunkt t, Vector3 richtung, Transform halter, bool ruhig = false)
        {
            if (t == Trennpunkt.Keiner || anim == null || !EnemyGore.Full) return false;
            Transform schnitt = anim.GetBoneTransform(Knochen(t));
            if (schnitt == null) return false;
            var glied = new HashSet<Transform>(schnitt.GetComponentsInChildren<Transform>(true));
            Material blut = null;
            Zustand zustand = anim.GetComponent<Zustand>();
            if (zustand == null) zustand = anim.gameObject.AddComponent<Zustand>();
            bool getrennt = false;
            Vector3 dir = richtung.sqrMagnitude > 1e-6f ? richtung.normalized : -anim.transform.forward;
            GameObject teil = null;
            // 22.10. (Prüfbild: das Bein trennte am Hüftgelenk ab – der ganze Oberschenkel hängt an einem Knochen): Oberschenkel und
            // Oberarm werden in der Mitte getrennt – die Haut dieses Knochens wird nach ihrer Lage entlang des Knochens geteilt.
            HumanBodyBones kb = KindVon(t);
            Transform kindKnochen = kb != HumanBodyBones.LastBone ? anim.GetBoneTransform(kb) : null;
            float anteil = kindKnochen == null ? 0f : Anteil(t);
            Vector3 schnittPunkt = anteil > 0f ? Vector3.Lerp(schnitt.position, kindKnochen.position, anteil) : schnitt.position;

            foreach (SkinnedMeshRenderer smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh src = smr.sharedMesh;
                if (src == null || !src.isReadable) continue;
                Transform[] bones = smr.bones;
                var imGlied = new bool[bones.Length];
                bool irgendein = false;
                for (int i = 0; i < bones.Length; i++) { imGlied[i] = bones[i] != null && glied.Contains(bones[i]); irgendein |= imGlied[i]; }
                if (!irgendein) continue;
                BoneWeight[] w = src.boneWeights;
                var gebacken = new Mesh { name = src.name + " (Teil)" };
                smr.BakeMesh(gebacken, true);
                Vector3[] lage = gebacken.vertices;
                int schnittIdx = System.Array.IndexOf(bones, schnitt);
                Vector3 p0 = smr.transform.InverseTransformPoint(schnitt.position);
                Vector3 ax = anteil > 0f ? smr.transform.InverseTransformPoint(kindKnochen.position) - p0 : Vector3.zero;
                float ax2 = Mathf.Max(1e-8f, ax.sqrMagnitude);
                var vGlied = new bool[src.vertexCount];
                for (int v = 0; v < vGlied.Length && v < w.Length; v++)
                {
                    BoneWeight b = w[v];
                    int best = b.boneIndex0; float bw = b.weight0;
                    if (b.weight1 > bw) { best = b.boneIndex1; bw = b.weight1; }
                    if (b.weight2 > bw) { best = b.boneIndex2; bw = b.weight2; }
                    if (b.weight3 > bw) best = b.boneIndex3;
                    vGlied[v] = best >= 0 && best < imGlied.Length && imGlied[best];
                    if (vGlied[v] && anteil > 0f && best == schnittIdx && v < lage.Length)
                        vGlied[v] = Vector3.Dot(lage[v] - p0, ax) / ax2 > anteil;   // obere Hälfte des Knochens bleibt am Körper
                }
                // Körper ohne das Glied (Kopie nur für diese Leiche), Glied aus der aktuellen Pose gebacken
                Mesh koerper = Object.Instantiate(src);
                koerper.name = src.name + " (getrennt)";
                int sub = src.subMeshCount;
                var teilTris = new List<int>[sub];
                bool teilHat = false;
                for (int s = 0; s < sub; s++)
                {
                    int[] tr = src.GetTriangles(s);
                    var bleibt = new List<int>(tr.Length);
                    teilTris[s] = new List<int>();
                    for (int i = 0; i < tr.Length; i += 3)
                    {
                        int n = (vGlied[tr[i]] ? 1 : 0) + (vGlied[tr[i + 1]] ? 1 : 0) + (vGlied[tr[i + 2]] ? 1 : 0);
                        List<int> ziel = n >= 2 ? teilTris[s] : bleibt;
                        ziel.Add(tr[i]); ziel.Add(tr[i + 1]); ziel.Add(tr[i + 2]);
                    }
                    koerper.SetTriangles(bleibt, s);
                    teilHat |= teilTris[s].Count > 0;
                }
                if (!teilHat) { Weg(koerper); Weg(gebacken); continue; }
                zustand.Netze.Add((smr, src, koerper));
                smr.sharedMesh = koerper;
                for (int s = 0; s < sub; s++) gebacken.SetTriangles(teilTris[s], s);
                gebacken.RecalculateBounds();

                if (teil == null)
                {
                    teil = new GameObject("Abgetrennt " + t);
                    teil.layer = 2;   // Ignore Raycast: stört weder Klicks noch Kamera
                    teil.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    teil.transform.SetParent(halter, true);
                    zustand.Objekte.Add(teil);
                    teil.AddComponent<TeilNetze>();
                }
                teil.GetComponent<TeilNetze>().Eigene.Add(gebacken);   // gehört dem Teil: wird mit ihm freigegeben (Zeitablauf, Budget, Respawn, Leiche weg)
                var go = new GameObject(smr.name);
                go.layer = 2;
                go.transform.SetParent(teil.transform, false);
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = gebacken;
                go.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                if (blut == null) blut = EnemyGore.Tint(smr.sharedMaterial, BlutFarbe);
                getrennt = true;
            }
            if (!getrennt) return false;

            // Waffe in der abgetrennten Hand: am (unsichtbaren) Handknochen ausblenden, eine Kopie fliegt mit dem Teil
            foreach (Renderer r in schnitt.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || !r.enabled) continue;
                bool unterKopie = false;   // Kinder eines schon kopierten Teils nicht doppelt kopieren
                for (Transform a = r.transform.parent; a != null && a != schnitt; a = a.parent)
                    if (a.GetComponent<MeshRenderer>() != null) { unterKopie = true; break; }
                r.enabled = false;
                zustand.Versteckt.Add(r);
                if (unterKopie) continue;
                GameObject kopie = Object.Instantiate(r.gameObject, r.transform.position, r.transform.rotation, teil.transform);
                foreach (Transform k in kopie.GetComponentsInChildren<Transform>(true)) { k.gameObject.layer = 2; foreach (Collider c in k.GetComponents<Collider>()) Weg(c); }
                foreach (Renderer kr in kopie.GetComponentsInChildren<Renderer>(true)) kr.enabled = true;
            }
            Material fleisch = EnemyGore.Tint(blut, FleischFarbe);
            // Schnittscheibe quer zum Glied, so dick wie das Glied (Hals ~11 cm, Oberschenkel ~16 cm)
            Transform kind = null;
            foreach (Transform c in schnitt) if (glied.Contains(c) && c.GetComponent<Renderer>() == null) { kind = c; break; }
            Vector3 achse = kind != null ? (kind.position - schnitt.position).normalized : (schnitt.position - (schnitt.parent != null ? schnitt.parent.position : schnitt.position - Vector3.up)).normalized;
            float dicke = Dicke(t);
            Transform stumpf = anteil > 0f ? schnitt : (schnitt.parent != null ? schnitt.parent : schnitt);
            zustand.Objekte.Add(Kappe(stumpf, schnittPunkt, achse, dicke, fleisch));   // Stumpf am Körper
            Kappe(teil.transform, schnittPunkt, achse, dicke, fleisch);                                                           // Schnittfläche am Teil

            Bounds grenzen = EnemyGore.LocalBounds(teil.transform);
            var box = teil.AddComponent<BoxCollider>();
            box.center = grenzen.center;
            box.size = Vector3.Max(grenzen.size, Vector3.one * 0.08f);
            var rb = teil.AddComponent<Rigidbody>();
            rb.mass = t == Trennpunkt.Kopf ? 4f : 6f;
            // 23.10. ruhig = Leichenzustand für späte Beobachter: das Teil liegt schon, kein neuer Wurf/Blutstoß (Codex-Plan Paket C)
            rb.linearVelocity = ruhig ? Vector3.zero : dir * Random.Range(2f, 3.5f) + Vector3.up * Random.Range(1.2f, 2.2f);
            rb.angularVelocity = ruhig ? Vector3.zero : Random.insideUnitSphere * 6f;

            if (Application.isPlaying && !ruhig)   // Splitter/Flecken zerstören sich zeitversetzt – nur im Spiel
            {
                EnemyGore.Burst(schnittPunkt, dir, blut, 14, 0.035f, 8f);
                EnemyGore.Stain(schnittPunkt, blut, 0.9f, Liegezeit);
            }
            GameObject weg = teil;
            Liegen(teil, () => Weg(weg));
            return true;
        }

        /// <summary>
        /// Loses Teil ins gemeinsame Budget (Mensch und Tier): nach <see cref="Liegezeit"/> oder wenn mehr als <see cref="MaxTeile"/>
        /// liegen (ältestes zuerst), wird <paramref name="abraeumen"/> aufgerufen – Menschenteile werden zerstört, Tierteile nur
        /// ausgeblendet (das Original wird beim Respawn wieder eingesetzt). Nur im Spiel.
        /// </summary>
        public static void Liegen(GameObject teil, System.Action abraeumen)
        {
            if (teil == null || !Application.isPlaying) return;
            Liegt l = teil.GetComponent<Liegt>();
            if (l == null) l = teil.AddComponent<Liegt>();
            l.Bis = Time.time + Liegezeit;
            l.Abraeumen = abraeumen;
            Liegend.Remove(l);
            Liegend.Add(l);
            while (Liegend.Count > MaxTeile) { if (Liegend[0] == null) Liegend.RemoveAt(0); else Liegend[0].Raeumen(); }
        }

        /// <summary>Teil aus dem Budget nehmen, ohne abzuräumen (Tier: Teil wird beim Respawn wieder angesetzt).</summary>
        public static void Abmelden(GameObject teil)
        {
            Liegt l = teil != null ? teil.GetComponent<Liegt>() : null;
            if (l == null) return;
            l.Abraeumen = null;
            Liegend.Remove(l);
            Weg(l);
        }

        /// <summary>Ein Teil im Budget: räumt sich nach Ablauf selbst ab.</summary>
        private sealed class Liegt : MonoBehaviour
        {
            public float Bis;
            public System.Action Abraeumen;

            private void Update()
            {
                if (Time.time >= Bis) Raeumen();
            }

            public void Raeumen()
            {
                Liegend.Remove(this);
                System.Action a = Abraeumen;
                Abraeumen = null;
                enabled = false;
                a?.Invoke();
            }

            private void OnDestroy() => Liegend.Remove(this);
        }

        /// <summary>Gebackene Teil-Meshes – gehören dem abgetrennten Teil und werden mit ihm freigegeben (Codex-Review 22.10.).</summary>
        [ExecuteAlways]
        private sealed class TeilNetze : MonoBehaviour
        {
            public readonly List<Mesh> Eigene = new List<Mesh>();

            private void OnDestroy()
            {
                foreach (Mesh m in Eigene) Weg(m);
                Eigene.Clear();
            }
        }

        /// <summary>Im Spiel Destroy, im Edit-Modus (Lab-Bilder, Tests) DestroyImmediate.</summary>
        private static void Weg(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        /// <summary>Fleischkappe (flache Scheibe quer zum Glied) an der Schnittstelle.</summary>
        /// <summary>Wo entlang des Knochens getrennt wird (0 = am Gelenk, 0,45 = Mitte des Oberschenkels).</summary>
        private static float Anteil(Trennpunkt t) =>
            t == Trennpunkt.OberschenkelL || t == Trennpunkt.OberschenkelR ? 0.45f : t == Trennpunkt.OberarmL || t == Trennpunkt.OberarmR ? 0.4f : 0f;

        /// <summary>Nächster Knochen der Kette (Knie, Ellbogen) – für die Lage des Schnitts in der Mitte.</summary>
        private static HumanBodyBones KindVon(Trennpunkt t)
        {
            switch (t)
            {
                case Trennpunkt.OberschenkelL: return HumanBodyBones.LeftLowerLeg;
                case Trennpunkt.OberschenkelR: return HumanBodyBones.RightLowerLeg;
                case Trennpunkt.OberarmL: return HumanBodyBones.LeftLowerArm;
                case Trennpunkt.OberarmR: return HumanBodyBones.RightLowerArm;
                default: return HumanBodyBones.LastBone;
            }
        }

        /// <summary>Durchmesser des Gliedes am Trennpunkt (m).</summary>
        private static float Dicke(Trennpunkt t)
        {
            switch (t)
            {
                case Trennpunkt.Kopf: return 0.11f;
                case Trennpunkt.OberarmL: case Trennpunkt.OberarmR: return 0.115f;   // etwas weiter als das Glied: deckt die Hautzacken am Schnitt
                case Trennpunkt.UnterarmL: case Trennpunkt.UnterarmR: return 0.085f;
                default: return 0.18f;
            }
        }

        private static GameObject Kappe(Transform parent, Vector3 pos, Vector3 achse, float dicke, Material mat)
        {
            var k = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            k.name = "Schnitt";
            Weg(k.GetComponent<Collider>());
            k.layer = 2;
            k.transform.SetParent(parent, true);
            k.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, achse.sqrMagnitude > 1e-6f ? achse : Vector3.up));
            float s = 1f / Mathf.Max(1e-4f, parent.lossyScale.x);
            k.transform.localScale = new Vector3(dicke, dicke * 0.3f, dicke) * s;   // flache Scheibe quer zum Glied
            var r = k.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return k;
        }

        /// <summary>Gegner wieder ganz machen (Wiederbelebung/Respawn desselben Objekts).</summary>
        public static void Wiederherstellen(GameObject gegner)
        {
            if (gegner == null) return;
            Zustand z = gegner.GetComponent<Zustand>();
            if (z != null) z.Wiederherstellen();
        }

        /// <summary>Was eine Zerstückelung an der Figur verändert hat – zum Zurücksetzen.</summary>
        [ExecuteAlways]   // OnDestroy auch im Edit-Modus (Lab-Bilder, Tests): Körperkopien nicht liegen lassen
        public sealed class Zustand : MonoBehaviour
        {
            // Kopie = die für diese Leiche erzeugte Körperkopie; gehört dem Zustand, unabhängig davon, ob der Renderer noch lebt
            public readonly List<(SkinnedMeshRenderer Smr, Mesh Original, Mesh Kopie)> Netze = new List<(SkinnedMeshRenderer, Mesh, Mesh)>();
            public readonly List<GameObject> Objekte = new List<GameObject>();
            public readonly List<Renderer> Versteckt = new List<Renderer>();

            public void Wiederherstellen()
            {
                foreach ((SkinnedMeshRenderer smr, Mesh original, Mesh kopie) in Netze)
                {
                    if (smr != null && smr.sharedMesh == kopie) smr.sharedMesh = original;
                    Weg(kopie);
                }
                foreach (GameObject o in Objekte) if (o != null) Weg(o);
                foreach (Renderer r in Versteckt) if (r != null) r.enabled = true;
                Netze.Clear(); Objekte.Clear(); Versteckt.Clear();
            }

            private void OnDestroy()
            {
                foreach ((SkinnedMeshRenderer _, Mesh _, Mesh kopie) in Netze) Weg(kopie);
                Netze.Clear();
            }
        }
    }
}
