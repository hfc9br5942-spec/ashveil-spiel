using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Audio
{
    public enum SfxId
    {
        Swing,
        SwingHeavy,
        HitStraw,
        HitHeavy,
        Gunshot,
        GunshotHeavy,
        Reload,
        EmptyClick,
        WeaponDraw,
        /// <summary>Wolfsgeheul (Rudelruf)</summary>
        Howl,
        /// <summary>Knurren / Biss</summary>
        Growl,
        /// <summary>Stoehnen der Verblichenen</summary>
        Moan,
        /// <summary>Stufenaufstieg: aufsteigender Glockenakkord</summary>
        LevelUp,
        /// <summary>Auftrag angenommen</summary>
        QuestAccept,
        /// <summary>Auftrag abgeschlossen</summary>
        QuestComplete,
        /// <summary>Muenzen (Kauf/Verkauf)</summary>
        Coins,
        /// <summary>Tiefe, verstimmte Kirchenglocke (Glockenhirte)</summary>
        Bell,
        /// <summary>Schwerer Stampfer / Aufprall eines Kolosses</summary>
        Stomp,
        /// <summary>Spucken / Nadelschuss (Nadelspeier)</summary>
        Spit,
        /// <summary>Platschen (Abtauchen)</summary>
        Splash,
        /// <summary>Sprengladung (Vorarbeiter Hask)</summary>
        Explosion,
        /// <summary>Bogen spannen (Knarzen der Wurfarme)</summary>
        BowDraw,
        /// <summary>Sehne schnellt vor</summary>
        BowRelease,
        /// <summary>Pfeil schlaegt ein</summary>
        ArrowHit,
        /// <summary>Zauber sammelt sich (Wirken)</summary>
        CastCharge,
        /// <summary>Zauber wird entlassen</summary>
        CastRelease,
        /// <summary>Zauber schlaegt ein</summary>
        SpellHit,
        /// <summary>Schlag prallt am Schild ab</summary>
        ShieldBlock,
        /// <summary>Ausweichrolle/-sprung</summary>
        Dodge
    }

    /// <summary>
    /// Erzeugt Platzhalter-Klaenge per Code (gefiltertes Rauschen + Sinus), damit Treffer schon
    /// ohne Sounddateien hoerbar sind. Sobald eine Waffe echte AudioClips hat, werden diese benutzt.
    /// </summary>
    public static class ProceduralSfx
    {
        private const int SampleRate = 44100;
        private static readonly Dictionary<SfxId, AudioClip> Cache = new Dictionary<SfxId, AudioClip>();
        private static System.Random _rng = new System.Random(1234);

        public static AudioClip Get(SfxId id)
        {
            if (Cache.TryGetValue(id, out AudioClip clip) && clip != null) return clip;
            clip = Build(id);
            Cache[id] = clip;
            return clip;
        }

        private static AudioClip Build(SfxId id)
        {
            switch (id)
            {
                case SfxId.Swing: return Whoosh("sfx_swing", 0.22f, 900f, 2600f, 0.55f);
                case SfxId.SwingHeavy: return Whoosh("sfx_swing_heavy", 0.34f, 500f, 1800f, 0.75f);
                case SfxId.HitStraw: return Impact("sfx_hit_straw", 0.16f, 170f, 110f, 0.5f, 2500f);
                case SfxId.HitHeavy: return Impact("sfx_hit_heavy", 0.28f, 95f, 45f, 0.85f, 1400f);
                case SfxId.Gunshot: return Shot("sfx_gunshot", 0.55f, 1f);
                case SfxId.GunshotHeavy: return Shot("sfx_gunshot_heavy", 0.8f, 1.35f);
                case SfxId.Reload: return Clicks("sfx_reload", new[] { 0.02f, 0.38f, 0.62f }, 0.8f);
                case SfxId.EmptyClick: return Clicks("sfx_empty", new[] { 0.01f }, 0.1f);
                case SfxId.WeaponDraw: return Whoosh("sfx_draw", 0.18f, 2500f, 5000f, 0.3f);
                case SfxId.Howl: return Voice("sfx_howl", 1.9f, 330f, 520f, 300f, 0.02f, 0.5f);
                case SfxId.Growl: return Voice("sfx_growl", 0.55f, 95f, 120f, 80f, 0.35f, 0.7f);
                case SfxId.Moan: return Voice("sfx_moan", 1.2f, 140f, 170f, 110f, 0.12f, 0.45f);
                case SfxId.LevelUp: return Chime("sfx_levelup", new[] { 392f, 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 1.9f, 0.5f);
                case SfxId.QuestAccept: return Chime("sfx_quest_accept", new[] { 440f, 587.33f }, 0.09f, 0.9f, 0.4f);
                case SfxId.QuestComplete: return Chime("sfx_quest_complete", new[] { 523.25f, 659.25f, 783.99f }, 0.1f, 1.4f, 0.45f);
                case SfxId.Coins: return Chime("sfx_coins", new[] { 2637f, 3136f, 2793f }, 0.045f, 0.35f, 0.25f);
                case SfxId.Bell: return Chime("sfx_bell", new[] { 98f, 146.8f, 103.8f }, 0.035f, 4.2f, 0.75f);
                case SfxId.Stomp: return Impact("sfx_stomp", 0.9f, 70f, 28f, 1f, 600f);
                case SfxId.Spit: return Whoosh("sfx_spit", 0.16f, 1800f, 4200f, 0.5f);
                case SfxId.Splash: return Whoosh("sfx_splash", 0.5f, 400f, 2500f, 0.8f);
                case SfxId.Explosion: return Impact("sfx_explosion", 1.4f, 55f, 22f, 1f, 2200f);
                case SfxId.BowDraw: return Voice("sfx_bow_draw", 0.45f, 62f, 78f, 70f, 0.6f, 0.22f);
                case SfxId.BowRelease: return Impact("sfx_bow_release", 0.24f, 190f, 120f, 0.6f, 3800f);
                case SfxId.ArrowHit: return Impact("sfx_arrow_hit", 0.12f, 260f, 150f, 0.45f, 4200f);
                case SfxId.CastCharge: return Voice("sfx_cast", 0.9f, 220f, 440f, 660f, 0.05f, 0.22f);
                case SfxId.CastRelease: return Whoosh("sfx_cast_release", 0.35f, 300f, 3200f, 0.6f);
                case SfxId.SpellHit: return Impact("sfx_spell_hit", 0.5f, 120f, 60f, 0.7f, 5200f);
                case SfxId.ShieldBlock: return Impact("sfx_shield_block", 0.26f, 330f, 210f, 0.9f, 6500f);
                case SfxId.Dodge: return Whoosh("sfx_dodge", 0.3f, 300f, 1300f, 0.45f);
                default: return Clicks("sfx_default", new[] { 0.01f }, 0.1f);
            }
        }

        private static float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);

        /// <summary>Glockentoene nacheinander angeschlagen (Grundton + unharmonische Obertoene, langes Ausklingen).</summary>
        private static AudioClip Chime(string name, float[] notes, float spacing, float length, float gain)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = Mathf.RoundToInt(k * spacing * SampleRate);
                float f = notes[k];
                for (int i = start; i < n; i++)
                {
                    float t = (float)(i - start) / SampleRate;
                    float env = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 400f);
                    float v = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-t * 6f)
                              + 0.2f * Mathf.Sin(2f * Mathf.PI * f * 5.4f * t) * Mathf.Exp(-t * 10f);
                    data[i] += v * env * gain / Mathf.Sqrt(notes.Length);
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            return Make(name, data);
        }

        /// <summary>Luftzug: Rauschen, dessen Filter-Frequenz ansteigt und abfaellt.</summary>
        private static AudioClip Whoosh(string name, float length, float fLow, float fHigh, float gain)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
                float cutoff = Mathf.Lerp(fLow, fHigh, Mathf.Sin(Mathf.PI * t));
                float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / SampleRate);
                lp += a * (Noise() - lp);
                lp2 += a * (lp - lp2);
                data[i] = (lp - lp2 * 0.6f) * env * gain * 2.2f;
            }
            return Make(name, data);
        }

        /// <summary>Aufprall: tiefer, abfallender Ton plus kurzer Rauschknall.</summary>
        private static AudioClip Impact(string name, float length, float f0, float f1, float gain, float noiseCutoff)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            float phase = 0f, lp = 0f;
            float a = Mathf.Clamp01(2f * Mathf.PI * noiseCutoff / SampleRate);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float k = (float)i / n;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2f * Mathf.PI * f / SampleRate;
                float tone = Mathf.Sin(phase) * Mathf.Exp(-t * 22f);
                lp += a * (Noise() - lp);
                float crack = lp * Mathf.Exp(-t * 60f) * 2.5f;
                data[i] = (tone * 0.9f + crack) * gain;
            }
            return Make(name, data);
        }

        /// <summary>Schuss: harter Knall, Donner und Nachhall.</summary>
        private static AudioClip Shot(string name, float length, float weight)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            float lp = 0f, lpBody = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise();
                lp += 0.35f * (noise - lp);
                lpBody += 0.04f / weight * (noise - lpBody);
                phase += 2f * Mathf.PI * (55f / weight) / SampleRate;
                float crack = noise * Mathf.Exp(-t * 180f);
                float body = lpBody * Mathf.Exp(-t * 9f / weight) * 4f;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 14f / weight) * 0.8f;
                float tail = lp * Mathf.Exp(-t * 5f) * 0.12f;
                data[i] = Mathf.Clamp((crack * 0.9f + body + boom + tail) * 0.8f, -1f, 1f);
            }
            return Make(name, data);
        }

        /// <summary>
        /// Stimme: Grundton mit Obertoenen, Tonhoehe steigt zur Mitte und faellt ab (Heulen),
        /// plus Rauschanteil (Knurren/Rauheit).
        /// </summary>
        private static AudioClip Voice(string name, float length, float fStart, float fPeak, float fEnd, float roughness, float gain)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float f = k < 0.4f ? Mathf.Lerp(fStart, fPeak, Mathf.SmoothStep(0f, 1f, k / 0.4f))
                                   : Mathf.Lerp(fPeak, fEnd, Mathf.SmoothStep(0f, 1f, (k - 0.4f) / 0.6f));
                f *= 1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 5.5f * i / SampleRate); // Vibrato
                phase += 2f * Mathf.PI * f / SampleRate;
                float tone = Mathf.Sin(phase) + 0.45f * Mathf.Sin(2f * phase) + 0.2f * Mathf.Sin(3f * phase);
                lp += 0.08f * (Noise() - lp);
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f));
                data[i] = Mathf.Clamp((tone * (1f - roughness) * 0.45f + lp * roughness * 3f * (0.6f + 0.4f * Mathf.Sin(phase * 0.5f))) * env * gain, -1f, 1f);
            }
            return Make(name, data);
        }

        /// <summary>Mechanische Klicks (Nachladen, leeres Magazin).</summary>
        private static AudioClip Clicks(string name, float[] times, float length)
        {
            int n = Mathf.CeilToInt(length * SampleRate);
            var data = new float[n];
            foreach (float start in times)
            {
                int s0 = Mathf.FloorToInt(start * SampleRate);
                float prev = 0f;
                for (int i = 0; i < 1800 && s0 + i < n; i++)
                {
                    float t = (float)i / SampleRate;
                    float noise = Noise();
                    float hp = noise - prev; // Hochpass: metallisch
                    prev = noise;
                    float ping = Mathf.Sin(2f * Mathf.PI * 2300f * t) * 0.4f;
                    data[s0 + i] += (hp * 0.6f + ping) * Mathf.Exp(-t * 320f) * 0.7f;
                }
            }
            return Make(name, data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Cache.Clear();
            _rng = new System.Random(1234);
        }
    }
}
