using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ashveil.Core;
using Ashveil.Rendering;
using Ashveil.Rendering.Sky;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest Dynamic Sky V1 (Phasen 1–6): Himmel in der Szene, derselbe Himmel auf Server und Client ohne
    /// Uebertragung, 30 Welt-Tage im Zeitraffer (Zahlen: Abwechslung, keine Spruenge, Monde, Naechte) und 120 Bilder
    /// (06/12/18/00 Uhr je Tag) mit Kontaktbogen (SkyMonthSimulationTest, § 79–81). Bilder: Logs/Ashveil/Himmel.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string SkyFlag = "himmel";
        private static bool _skyMode;

        private IEnumerator SkySuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(EmoteA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            if (a == null) yield break;
            yield return new WaitForSecondsRealtime(1f);
            Zeitraffer(true);   // Sternschnuppen je echter Sekunde und „2 Spielminuten = 10 s“ sind im alten Tempo gedacht

            // ---------------------------------------------------------- Aufbau
            Line("");
            Line("[Himmel in der Szene]");
            SkyDirector sky = SkyDirector.Active;
            Check(sky != null && sky.Climate != null, "SkyDirector mit Klima-Daten aktiv");
            if (sky == null || sky.Climate == null) yield break;
            SkyClimateProfile climate = sky.Climate;
            Check(RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Ashveil/DynamicSky", $"Himmelsmaterial nutzt den eigenen Shader ({(RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "-")})");
            Check(climate.clouds.Length == 7 && climate.clouds.All(c => c != null), $"7 Himmelszustaende als Daten ({string.Join(", ", climate.clouds.Where(c => c != null).Select(c => c.state))})");
            Check(climate.moonA != null && climate.moonB != null && climate.moonA.orbitalPeriodDays != climate.moonB.orbitalPeriodDays,
                $"Zwei Monde mit eigenen Bahnen ({climate.moonA?.displayName} {climate.moonA?.orbitalPeriodDays:0.#} Tage, {climate.moonB?.displayName} {climate.moonB?.orbitalPeriodDays:0.#} Tage)");
            int conStars = climate.constellations.Sum(c => c.stars.Length);
            Check(climate.constellations.Length >= 8 && conStars <= SkyDirector.MaxConstellationStars, $"{climate.constellations.Length} eigene Sternbilder ({conStars} Sterne)");
            Info("Sternbilder: " + string.Join(", ", climate.constellations.Select(c => c.name)));
            Info($"Darstellungsstufe: {SkyDirector.Quality}");
            int ev0 = sky.Evaluations;
            yield return new WaitForSecondsRealtime(2f);
            int perSecond = (sky.Evaluations - ev0) / 2;
            Check(perSecond >= 2 && perSecond <= 8, $"Zustand wird nur {perSecond}× je Sekunde berechnet (nicht jedes Bild)");

            // ---------------------------------------------------------- Gleicher Himmel
            Line("");
            Line("[Ein Himmel für alle – ohne Übertragung]");
            string exe = ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, EmoteB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(EmoteB)) != null && b.IsReady, 120f);
            Check(b != null, "Krisbot (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "sky-state");
            Check(_last.ok, "Krisbot hat einen Himmel");
            if (_last.ok)
            {
                double tb = double.Parse(Field(_last.data, "t="), CultureInfo.InvariantCulture);
                SkyRuntimeState mine = sky.Evaluate(tb);
                float dCov = Mathf.Abs(ParseAfter(_last.data, "cov=") - mine.CloudCoverage);
                Vector3 mb = new Vector3(ParseAfter(_last.data, "ax="), ParseAfter(_last.data, "ay="), ParseAfter(_last.data, "az="));
                float dMoon = Vector3.Angle(mb, mine.MoonADirection);
                float dPhase = Mathf.Abs(ParseAfter(_last.data, "pa=") - mine.MoonAPhase);
                Check(dCov < 1e-3f && dMoon < 0.05f && dPhase < 1e-3f && Field(_last.data, "type=") == mine.CloudType.ToString(),
                    $"Krisbot berechnet zur selben Weltzeit denselben Himmel ({mine.CloudType}, Bedeckung {mine.CloudCoverage:0.00}, Mond A {mine.MoonAPhase:P0}; Abweichung {dMoon:0.000}°)");
                double diffMin = Math.Abs(tb - WorldClock.TotalHours) * 60.0;
                Info($"Uhrzeit Krisbot ↔ Server: {diffMin:0.0} Spielminuten Abstand – der Himmel folgt nur der gemeinsamen Uhr");
            }
            SkyRuntimeState e1 = sky.Evaluate(1234.5), e2 = sky.Evaluate(1234.5);
            Check(e1.CloudCoverage == e2.CloudCoverage && e1.MoonBDirection == e2.MoonBDirection, "Gleiche Zeit ergibt immer denselben Himmel (deterministisch)");

            // ---------------------------------------------------------- Pass 11 Gebietsluft
            Line("");
            Line("[Gebietsluft – Startlager und Glockenmoor (Pass 11)]");
            yield return BiomeSection(sky);

            // ---------------------------------------------------------- Pass 12 Sternschnuppen
            Line("");
            Line("[Sternschnuppen (Pass 12)]");
            yield return MeteorSection(sky, b);

            // ---------------------------------------------------------- 30 Tage in Zahlen
            SkyDirector.IgnoreLocal = true; // Monatsbilder zeigen nur den globalen Himmel
            SkyDirector.ViewerOverride = null;
            sky.SnapLocal();
            Line("");
            Line("[30 Welt-Tage im Zeitraffer – Zahlen]");
            double start = Math.Floor(WorldClock.TotalHours / 24.0 + 1.0) * 24.0; // ab dem naechsten Tag
            yield return SkyMonthNumbers(sky, start);

            // ---------------------------------------------------------- 30 Tage in Bildern
            Line("");
            Line("[30 Welt-Tage im Zeitraffer – Bilder]");
            yield return SkyMonthPictures(sky, start);

            SkyDirector.IgnoreLocal = false;
            sky.SnapLocal();
            // Uhr fuer alle wieder auf gestaltetes Nachmittagslicht
            NetworkWorldTime.ServerJumpTo((float)WorldClock.DefaultStartHour);
            Zeitraffer(false);
        }

        private IEnumerator SkyMonthNumbers(SkyDirector sky, double start)
        {
            const double step = 2.0 / 60.0; // 2 Spielminuten
            int samples = (int)(30 * 24 / step);
            var states = new Dictionary<SkyState, int>();
            float maxCovStep = 0f, maxMoonStep = 0f, maxTransStep = 0f;
            double worstAt = 0;
            SkyRuntimeState prev = sky.Evaluate(start);
            float minPA = 1f, maxPA = 0f, minPB = 1f, maxPB = 0f;
            for (int i = 1; i <= samples; i++)
            {
                double t = start + i * step;
                SkyRuntimeState s = sky.Evaluate(t);
                float dc = Mathf.Abs(s.CloudCoverage - prev.CloudCoverage);
                if (dc > maxCovStep) { maxCovStep = dc; worstAt = t; }
                maxMoonStep = Mathf.Max(maxMoonStep, Vector3.Angle(s.MoonADirection, prev.MoonADirection), Vector3.Angle(s.MoonBDirection, prev.MoonBDirection));
                maxTransStep = Mathf.Max(maxTransStep, Mathf.Abs(s.SunTransmittance - prev.SunTransmittance));
                SkyState dominant = s.CloudBlend >= 0.5f ? s.CloudType : s.CloudFrom;
                states[dominant] = states.TryGetValue(dominant, out int n) ? n + 1 : 1;
                minPA = Mathf.Min(minPA, s.MoonAPhase); maxPA = Mathf.Max(maxPA, s.MoonAPhase);
                minPB = Mathf.Min(minPB, s.MoonBPhase); maxPB = Mathf.Max(maxPB, s.MoonBPhase);
                prev = s;
                if (i % 3000 == 0) yield return null;
            }
            float share(SkyState st) => states.TryGetValue(st, out int n) ? n / (float)samples : 0f;
            Info("Anteile: " + string.Join(" · ", Enum.GetValues(typeof(SkyState)).Cast<SkyState>().Select(st => $"{st} {share(st):P0}")));
            Check(states.Count >= 5, $"Abwechslung: {states.Count} verschiedene Himmelszustände im Monat");
            Check(share(SkyState.Dramatic) <= 0.15f, $"Dramatische Wolken bleiben selten ({share(SkyState.Dramatic):P0}, höchstens 15 %)");
            Check(share(SkyState.Overcast) <= 0.35f && share(SkyState.Clear) + share(SkyState.ClearCirrus) >= 0.12f,
                $"Graue und klare Tage im Gleichgewicht (bedeckt {share(SkyState.Overcast):P0}, klar {share(SkyState.Clear) + share(SkyState.ClearCirrus):P0})");
            Check(maxCovStep < 0.02f, $"Keine springenden Wolken: größte Änderung der Bedeckung {maxCovStep:0.000} je 2 Spielminuten (Tag {(worstAt - start) / 24.0 + 1:0.0})");
            Check(maxTransStep < 0.06f, $"Kein Lichtflackern: Sonnendurchlass ändert sich höchstens um {maxTransStep:0.000} je 2 Spielminuten (10 s)");
            Check(maxMoonStep < 1.5f, $"Keine springenden Monde: höchstens {maxMoonStep:0.00}° je 2 Spielminuten");
            Check(minPA < 0.05f && maxPA > 0.95f, $"Mond A durchläuft alle Phasen ({minPA:0.00}–{maxPA:0.00})");
            Check(maxPB - minPB > 0.8f, $"Mond B wandert durch fast alle Phasen ({minPB:0.00}–{maxPB:0.00}, Umlauf länger als ein Monat)");

            // Monatswechsel: weich
            double monthStart = Math.Ceiling(start / (ClimateOf(sky).monthDays * 24.0)) * ClimateOf(sky).monthDays * 24.0;
            float jump = Mathf.Abs(sky.Evaluate(monthStart + 0.01).CloudCoverage - sky.Evaluate(monthStart - 0.01).CloudCoverage);
            Check(jump < 0.01f, $"Monatswechsel ohne Sprung (Tag {monthStart / 24.0:0}, Δ {jump:0.000})");

            // Naechte um Mitternacht
            int moonless = 0, both = 0, clearNights = 0, meteorNights = 0;
            var dayLines = new List<string>();
            for (int d = 0; d < 30; d++)
            {
                double day = start + d * 24.0;
                SkyRuntimeState m = sky.Evaluate(day + 24.0); // 00:00 der folgenden Nacht
                bool aUp = m.MoonAElevation > 3f && m.MoonAIllumination > 0.1f, bUp = m.MoonBElevation > 3f && m.MoonBIllumination > 0.1f;
                if (!aUp && !bUp) moonless++;
                if (aUp && bUp) both++;
                if (m.CloudCoverage < 0.25f) clearNights++;
                string S(double h) { SkyRuntimeState x = sky.Evaluate(day + h); return (x.CloudBlend >= 0.5f ? x.CloudType : x.CloudFrom).ToString(); }
                dayLines.Add($"Tag {d + 1,2}: 06 {S(6),-11} 12 {S(12),-11} 18 {S(18),-11} 00 {S(24),-11} · Mond A {(aUp ? "sichtbar" : "unter/dunkel"),-12} {m.MoonAIllumination:P0} · Mond B {(bUp ? "sichtbar" : "unter/dunkel"),-12} {m.MoonBIllumination:P0}");
            }
            Check(moonless >= 2 && moonless <= 25, $"Dunkle Nächte ohne sichtbaren Mond gibt es ({moonless} von 30)");
            Check(both >= 1, $"Nächte mit beiden Monden am Himmel ({both} von 30)");
            Check(clearNights >= 4, $"Klare Nächte für den Sternenhimmel ({clearNights} von 30)");
            for (int d = 0; d < 30; d++)
                if (sky.NextShower(start + d * 24.0, 0, out double ss, out _) && ss >= start + d * 24.0 && ss < start + (d + 1) * 24.0 + 6.0) meteorNights++;
            Info($"Sternschnuppenschauer in diesem Monat: {meteorNights} Nacht/Nächte (Zufall aus dem Seed, im Mittel etwa alle 20 Tage)");
            foreach (string l in dayLines) Info(l);
        }

        private static SkyClimateProfile ClimateOf(SkyDirector d) => d.Climate;

        // ================================================================== Pass 11

        private IEnumerator BiomeSection(SkyDirector sky)
        {
            var vols = BiomeAtmosphereVolume.All.Where(v => v != null && v.Profile != null).ToList();
            BiomeAtmosphereVolume moor = vols.FirstOrDefault(v => v.Profile.displayName == "Glockenmoor");
            BiomeAtmosphereVolume camp = vols.FirstOrDefault(v => v.Profile.displayName == "Startlager");
            Check(moor != null && camp != null, $"Gebiete mit eigener Luft in der Welt: {string.Join(", ", vols.Select(v => v.Profile.displayName))}");
            if (moor == null || camp == null) yield break;
            Vector3 mc = moor.transform.position, cc = camp.transform.position;

            // Raeumlich weich: Anteil faellt ueber die Blendzone stetig ab
            float w0 = moor.Weight(mc), wHalf = moor.Weight(mc + Vector3.right * (moor.Radius + moor.Falloff * 0.5f)), wOut = moor.Weight(mc + Vector3.right * (moor.Radius + moor.Falloff + 1f));
            float maxStep = 0f;
            for (float x = 0f; x < moor.Radius + moor.Falloff + 10f; x += 1f)
                maxStep = Mathf.Max(maxStep, Mathf.Abs(moor.Weight(mc + Vector3.right * (x + 1f)) - moor.Weight(mc + Vector3.right * x)));
            Check(w0 > 0.99f && wHalf > 0.35f && wHalf < 0.65f && wOut < 0.01f && maxStep < 0.04f,
                $"Weiche Blendzone am Moorrand ({moor.Falloff:0} m breit, halbe Wirkung in der Mitte {wHalf:0.00}, größter Sprung je Meter {maxStep:0.000})");

            // Zeitlich weich: vom Lager ins Moor
            SkyDirector.IgnoreLocal = false;
            SkyDirector.ViewerOverride = cc + Vector3.up * 2f;
            sky.SnapLocal();
            float fogCamp = SkyDirector.Local.FogFactor;
            string nameCamp = SkyDirector.Local.Name;
            float covGlobal = sky.State.CloudCoverage;
            LocalAtmosphere target = SkyDirector.Sample(mc);
            SkyDirector.ViewerOverride = mc + Vector3.up * 2f;
            yield return new WaitForSecondsRealtime(1f);
            float fog1 = SkyDirector.Local.FogFactor;
            yield return new WaitForSecondsRealtime(moor.Profile.blendSeconds + 2f);
            float fogMoor = SkyDirector.Local.FogFactor;
            sky.Refresh(WorldClock.TotalHours);
            SkyRuntimeState eff = sky.EffectiveState, glob = sky.State;
            Check(nameCamp == "Startlager" && Mathf.Abs(fogCamp - 1f) < 0.05f, $"Im Startlager gilt die Lagerluft ({nameCamp}, Nebel ×{fogCamp:0.00})");
            Check(fog1 > fogCamp + 0.02f && fog1 < target.FogFactor - 0.2f, $"Beim Betreten des Moors wird der Nebel langsam dichter (nach 1 s ×{fog1:0.00}, Ziel ×{target.FogFactor:0.00}) – kein harter Wechsel");
            Check(SkyDirector.Local.Name == "Glockenmoor" && Mathf.Abs(fogMoor - target.FogFactor) < 0.08f,
                $"Im Glockenmoor: dichterer, grünlicher Dunst (Nebel ×{fogMoor:0.00}, Dunst +{eff.HorizonHaze - glob.HorizonHaze:0.00}, Bedeckung {glob.CloudCoverage:0.00} → {eff.CloudCoverage:0.00}, Sonne ×{target.SunTransmission:0.00})");
            Check(Mathf.Abs(glob.CloudCoverage - sky.Evaluate(glob.TotalHours).CloudCoverage) < 1e-5f && Mathf.Abs(covGlobal - glob.CloudCoverage) < 0.05f,
                "Der globale Himmel bleibt unverändert – das Gebiet verschiebt nur die eigene Sicht (kein Netzverkehr)");

            // Bilder: gleicher Blick, gleiche Zeit – Lager und Moor
            Camera cam = Camera.main;
            if (cam != null)
            {
                string dir = ProjectPaths.Logs("Himmel");
                Directory.CreateDirectory(dir);
                Vector3 pos0 = cam.transform.position;
                Quaternion rot0 = cam.transform.rotation;
                float Ground(Vector3 p) => WorldGround.SampleHeight(p, 0f);
                (string file, Vector3 at, Vector3 look)[] shots =
                {
                    ("Gebiet_Startlager.png", cc + new Vector3(-12f, 0f, -24f), cc + new Vector3(10f, 0f, 60f)),
                    ("Gebiet_Glockenmoor.png", mc + new Vector3(-30f, 0f, -110f), mc + new Vector3(20f, 0f, 40f))
                };
                int ok = 0;
                foreach (var sh in shots)
                {
                    Vector3 eye = sh.at; eye.y = Mathf.Max(Ground(eye), 0f) + 7f;
                    Vector3 look = sh.look; look.y = Mathf.Max(Ground(look), 0f) + 9f;
                    SkyDirector.ViewerOverride = eye;
                    sky.SnapLocal();
                    yield return null;
                    yield return null;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    Texture2D tex = Grab(cam, 960, 540);
                    if (tex == null) continue;
                    File.WriteAllBytes(Path.Combine(dir, sh.file), tex.EncodeToPNG());
                    Destroy(tex);
                    ok++;
                }
                cam.transform.SetPositionAndRotation(pos0, rot0);
                Check(ok == 2, "Bilder Gebiet_Startlager.png und Gebiet_Glockenmoor.png (Logs/Ashveil/Himmel)");
            }
            SkyDirector.ViewerOverride = null;
            sky.SnapLocal();
        }

        // ================================================================== Pass 12

        private IEnumerator MeteorSection(SkyDirector sky, NetworkInventory b)
        {
            var ci = CultureInfo.InvariantCulture;
            double sph = WorldClock.SecondsPerHour;
            double now = WorldClock.TotalHours;
            // Klare, mondlose Nacht suchen (fuer die Zaehlung der Einzelmeteore)
            double night = -1;
            for (int d = 1; d < 60 && night < 0; d++)
            {
                double t = Math.Floor(now / 24.0 + d) * 24.0 + 0.5;
                if (!sky.ShowerAt(t, out _, out _)) night = t;
            }
            int sporadic = sky.CountMeteors(night * sph, (night + 1.0) * sph, out int sporShower);
            double noon = Math.Floor(now / 24.0 + 1) * 24.0 + 12.0;
            int day = sky.CountMeteors(noon * sph, (noon + 1.0) * sph, out _);
            Check(sporadic >= 3 && sporadic <= 20 && sporShower == 0, $"Einzelne Sternschnuppen in einer normalen Nacht: {sporadic} je Spielstunde (00:30–01:30)");
            Check(day == 0, "Tagsüber keine Sternschnuppen");

            bool found = sky.NextShower(now, 120, out double ss, out double se);
            Check(found, found ? $"Nächster Sternschnuppenschauer aus dem Seed: Tag {Math.Floor(ss / 24.0)}, {Hour(ss)}–{Hour(se)} Uhr ({se - ss:0.0} Spielstunden)" : "Sternschnuppenschauer im Kalender");
            if (!found) yield break;
            // Haeufigkeit ueber ein Jahr (deterministisch)
            int showers = 0;
            double y0 = Math.Floor(now / 24.0) * 24.0, cur = y0;
            while (sky.NextShower(cur, 400, out double a, out double e) && a < y0 + 360 * 24.0) { showers++; cur = e + 1.0; }
            Check(showers >= 6 && showers <= 40, $"Schauer bleiben selten: {showers} in 360 Tagen");

            double peak = (ss + se) * 0.5;
            int inPeak = sky.CountMeteors((peak - 0.5) * sph, (peak + 0.5) * sph, out int peakShower);
            Check(inPeak >= 60 && peakShower > inPeak / 2, $"Im Höhepunkt: {inPeak} Sternschnuppen je Spielstunde, {peakShower} davon aus dem Radianten");
            int again = sky.CountMeteors((peak - 0.5) * sph, (peak + 0.5) * sph, out _);
            Check(again == inPeak, "Gleiche Zeit ergibt dieselben Sternschnuppen (deterministisch)");

            if (b != null)
            {
                string arg = ((peak - 0.5) * sph).ToString("R", ci) + ";" + ((peak + 0.5) * sph).ToString("R", ci);
                yield return Ask(b, "sky-meteors", arg);
                int nb = _last.ok ? (int)ParseAfter(_last.data, "n=") : -1;
                Check(nb == inPeak, $"Krisbot sieht dieselben Sternschnuppen ohne Übertragung ({nb} = {inPeak})");
            }

            // Bild: Schauer am Himmel (Blick zum Radianten, mehrere Aufnahmen uebereinandergelegt)
            Camera cam = Camera.main;
            if (cam == null) yield break;
            SkyDirector.IgnoreLocal = true;
            sky.SnapLocal();
            double t0 = WorldClock.TotalHours;
            WorldClock.Set(peak, WorldClock.Now, WorldClock.SecondsPerHour);
            sky.Refresh(peak);
            yield return null;
            yield return null;
            SkyRuntimeState st = sky.State;
            Check(st.ActiveSkyEvent == "meteor_shower", $"Himmelsereignis aktiv: {(st.ActiveSkyEvent == "" ? "-" : st.ActiveSkyEvent)} (Bedeckung {st.CloudCoverage:0.00})");
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            Vector3 eye = new Vector3(150f, 0f, 260f);
            eye.y = WorldGround.SampleHeight(eye, 0f) + 30f;
            // Blick: Richtung mit den meisten Meteoren der naechsten Sekunden
            var buf = new SkyDirector.Meteor[8];
            Vector3 aim = Vector3.zero;
            double sec0 = peak * sph;
            for (int k = 0; k < 40; k++)
            {
                int n = sky.ActiveMeteors(sec0 + k * 0.5, buf);
                for (int i = 0; i < n; i++) aim += buf[i].From + buf[i].To;
            }
            if (aim.sqrMagnitude < 1e-4f) aim = new Vector3(0f, 1f, 1f);
            aim.Normalize();
            if (aim.y < 0.35f) { aim.y = 0.35f; aim.Normalize(); }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim));
            cam.fieldOfView = 95f;
            const int W = 960, H = 540;
            var sum = new Color[W * H];
            int frames = 0, withMeteor = 0, maxVisible = 0;
            float endAt = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < endAt && frames < 400)
            {
                yield return null;
                if (sky.VisibleMeteors > 0) withMeteor++;
                maxVisible = Mathf.Max(maxVisible, sky.VisibleMeteors);
                if (frames % 2 == 0)
                {
                    // Die Spielerkamera setzt sich jedes Bild zurueck – Blick vor jeder Aufnahme neu setzen
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim));
                    cam.fieldOfView = 95f;
                    Texture2D tex = Grab(cam, W, H);
                    if (tex != null)
                    {
                        Color[] px = tex.GetPixels();
                        for (int i = 0; i < px.Length; i++) sum[i] = frames == 0 ? px[i] : new Color(Mathf.Max(sum[i].r, px[i].r), Mathf.Max(sum[i].g, px[i].g), Mathf.Max(sum[i].b, px[i].b));
                        Destroy(tex);
                    }
                }
                frames++;
            }
            var outTex = new Texture2D(W, H, TextureFormat.RGB24, false);
            outTex.SetPixels(sum);
            outTex.Apply();
            File.WriteAllBytes(ProjectPaths.Logs("Himmel/Sternschnuppen_Schauer.png"), outTex.EncodeToPNG());
            Destroy(outTex);
            Check(withMeteor > 0, $"Sternschnuppen am Himmel sichtbar ({withMeteor} von {frames} Bildern mit Leuchtspur, höchstens {maxVisible} gleichzeitig) – Sternschnuppen_Schauer.png (20 s übereinandergelegt)");
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            WorldClock.Set(Math.Max(t0, WorldClock.TotalHours), WorldClock.Now, WorldClock.SecondsPerHour);
            SkyDirector.IgnoreLocal = false;
            sky.SnapLocal();
        }

        private static string Hour(double total)
        {
            double h = total - Math.Floor(total / 24.0) * 24.0;
            return $"{(int)h:00}:{(int)((h - Math.Floor(h)) * 60):00}";
        }

        private IEnumerator SkyMonthPictures(SkyDirector sky, double start)
        {
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "Kamera für Himmelsbilder"); yield break; }
            string dir = ProjectPaths.Logs("Himmel/Monat");
            Directory.CreateDirectory(dir);
            const int W = 384, H = 216;
            var sheets = new[] { new Texture2D(W * 4, H * 15, TextureFormat.RGB24, false), new Texture2D(W * 4, H * 15, TextureFormat.RGB24, false) };
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            // Aussichtspunkt noerdlich des Lagers, ueber den Baeumen (freier Himmel in alle Richtungen)
            Vector3 eye = new Vector3(150f, 0f, 260f); // weit nordoestlich: der Riese steht nicht im Bild
            eye.y = WorldGround.SampleHeight(eye, 0f) + 30f;
            (float hour, float yaw, float pitch, float fov)[] views = { (6f, 95f, -14f, 72f), (12f, 200f, -28f, 72f), (18f, 265f, -14f, 72f), (24f, 180f, -60f, 100f) };
            int shots = 0;
            double total0 = WorldClock.TotalHours;
            for (int d = 0; d < 30; d++)
            {
                for (int v = 0; v < views.Length; v++)
                {
                    double t = start + d * 24.0 + views[v].hour;
                    WorldClock.Set(t, WorldClock.Now, WorldClock.SecondsPerHour);
                    sky.Refresh(t);
                    yield return null; // DayNightCycle setzt Licht und Umgebung
                    yield return null;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(views[v].pitch, views[v].yaw, 0f));
                    cam.fieldOfView = views[v].fov;
                    Texture2D tex = Grab(cam, W, H);
                    if (tex == null) continue;
                    File.WriteAllBytes(Path.Combine(dir, $"Tag_{d + 1:00}_{(int)views[v].hour % 24:00}00.png"), tex.EncodeToPNG());
                    Texture2D sheet = sheets[d / 15];
                    sheet.SetPixels(v * W, (14 - d % 15) * H, W, H, tex.GetPixels());
                    Destroy(tex);
                    shots++;
                }
            }
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            for (int i = 0; i < 2; i++)
            {
                sheets[i].Apply();
                File.WriteAllBytes(ProjectPaths.Logs($"Himmel/Monat_Tage_{i * 15 + 1:00}-{i * 15 + 15:00}.png"), sheets[i].EncodeToPNG());
                Destroy(sheets[i]);
            }
            Check(shots == 120, $"{shots} Himmelsbilder (30 Tage × 06/12/18/00 Uhr) und 2 Kontaktbögen (Logs/Ashveil/Himmel)");
            Info("Kontaktbogen: Zeilen = Tage, Spalten = 06 Uhr (Osten), 12 Uhr (Süden), 18 Uhr (Westen), 00 Uhr (Nachthimmel, weit)");
            WorldClock.Set(Math.Max(total0, WorldClock.TotalHours), WorldClock.Now, WorldClock.SecondsPerHour);
        }

        private static Texture2D Grab(Camera cam, int w, int h)
        {
            RenderTexture prevT = cam.targetTexture, prevA = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Himmelsbild fehlgeschlagen: " + e.Message);
                return null;
            }
            finally
            {
                cam.targetTexture = prevT;
                RenderTexture.active = prevA;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
