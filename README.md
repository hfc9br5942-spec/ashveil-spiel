# ASHVEIL – Spielcode (Arbeitsstand für Flo)

Dieses Repository enthält **nur den programmierten Spielcode** von ASHVEIL (Unity 6.6, URP 17.6, FishNet 4.7.3, server-autoritatives Online-Rollenspiel). Planung, Weltentwürfe, Grafiken, Modelle und Klänge liegen nicht hier.

**Vertraulich:** Bitte nichts davon weitergeben, veröffentlichen oder in andere Projekte übernehmen. Das Projekt gehört Max.

## Was drin ist
- `Assets/_Ashveil/Scripts/`: der gesamte eigene C#-Code (≈ 680 Dateien) samt `.asmdef` und Shadern:
  - **Gameplay**: Figuren und Bewegung, Kampf, Fähigkeiten, Zielwahl, Gegner, Begleiter, Reittiere, Aufträge, Gegenstände, Meisterschaften, Gruppen und Soziales, Story-Zustände, Kamera.
  - **Networking**: FishNet-Schicht (Spieler, Kampf, Inventar, Fortschritt, Gegnerdirektor, Interessenbereiche, Spielstand und Persistenz, Wiederverbinden).
  - **UI**: HUD, Aktionsleiste, Fenster für Aufträge, Gegenstände, Charaktererstellung, Einstellungen und Fotomodus.
  - **World**: Regionen, Wegpunkte, Weltzellen, Dungeons, Gebietsregeln.
  - **Data**: Datentypen für Gegenstände, Fähigkeiten, Gegner und Aufträge.
  - **Editor** und **EditorTests**: Editor-Werkzeuge und EditMode-Tests.
- `Packages/manifest.json`: die Unity-Pakete. FishNet wird dort als lokale Datei `com.firstgeargames.fishnet-4.7.3.tgz` erwartet und muss selbst aus der offiziellen Quelle (FishNet 4.7.3) besorgt werden.

## Was fehlt
Szenen, Modelle, Texturen, Klänge und Projekteinstellungen fehlen. Das Projekt lässt sich deshalb nicht direkt starten. Es ist zum Lesen, Mitdenken und Programmieren an einzelnen Systemen gedacht.

## Mitarbeiten
- Arbeite auf einem eigenen Zweig (zum Beispiel `flo/<thema>`) und stelle Änderungen als Pull Request ein.
- Max übernimmt Beiträge nach Prüfung in das Hauptprojekt.
- Regeln im Code: Der Server entscheidet alles Spielrelevante (Clients wünschen nur). Daten kommen aus Definitionen statt aus fest eingetragenen Werten. Zu jeder Regel gehört ein EditMode-Test.
