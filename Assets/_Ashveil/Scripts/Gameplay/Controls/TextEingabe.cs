using System;
using System.Text;

namespace Ashveil.Gameplay.Controls
{
    /// <summary>
    /// Einzeiliges Eingabefeld als reine Logik (UI-204; Spieltest r2, Max 09.10.2026: „meinen Namen am Charakter speichern“
    /// ging nicht). Kennt einen markierten Text – einen gewürfelten Vorschlag oder Strg+A –, den der erste Tastendruck
    /// ersetzt, einen Zeichenfilter und eine Höchstlänge. Anzeige, Fokus und Tasten liegen bei der Oberfläche.
    /// </summary>
    public sealed class TextEingabe
    {
        private readonly StringBuilder _text = new StringBuilder();
        private readonly Func<char, bool> _erlaubt;

        public TextEingabe(int maxLaenge, Func<char, bool> erlaubt = null)
        {
            MaxLaenge = Math.Max(1, maxLaenge);
            _erlaubt = erlaubt;
        }

        public int MaxLaenge { get; }
        public string Text => _text.ToString();
        public int Laenge => _text.Length;

        /// <summary>Der ganze Text ist markiert (Vorschlag des Würfels oder Strg+A): der nächste Tastendruck ersetzt ihn.</summary>
        public bool Markiert { get; private set; }

        /// <summary>Text setzen. alsVorschlag = markiert, das erste getippte Zeichen ersetzt ihn.</summary>
        public void Setzen(string text, bool alsVorschlag)
        {
            _text.Clear();
            Markiert = false;
            if (!string.IsNullOrEmpty(text))
                foreach (char c in text)
                    if (Erlaubt(c) && _text.Length < MaxLaenge) _text.Append(c);
            Markiert = alsVorschlag && _text.Length > 0;
        }

        public void Leeren()
        {
            _text.Clear();
            Markiert = false;
        }

        /// <summary>Strg+A: alles markieren (nur wenn etwas dasteht).</summary>
        public void AllesMarkieren() => Markiert = _text.Length > 0;

        /// <summary>Getippte Zeichen anwenden. Nicht erlaubte Zeichen fallen weg; ein markierter Text wird ersetzt. true = Text geändert.</summary>
        public bool Tippen(string zeichen)
        {
            if (string.IsNullOrEmpty(zeichen)) return false;
            bool geaendert = false;
            foreach (char c in zeichen)
            {
                if (!Erlaubt(c)) continue;
                if (Markiert)
                {
                    _text.Clear();
                    Markiert = false;
                    geaendert = true;
                }
                if (_text.Length >= MaxLaenge) continue;
                _text.Append(c);
                geaendert = true;
            }
            return geaendert;
        }

        /// <summary>Einfügen (Strg+V): wie Tippen – nur erlaubte Zeichen, bis zur Höchstlänge, Markiertes wird ersetzt.</summary>
        public bool Einfuegen(string inhalt) => Tippen(inhalt);

        /// <summary>Rücktaste: Markiertes ganz löschen, sonst das letzte Zeichen. true = Text geändert.</summary>
        public bool Loeschen()
        {
            if (Markiert)
            {
                bool hatte = _text.Length > 0;
                _text.Clear();
                Markiert = false;
                return hatte;
            }
            if (_text.Length == 0) return false;
            _text.Length--;
            return true;
        }

        private bool Erlaubt(char c) => !char.IsControl(c) && (_erlaubt == null || _erlaubt(c));
    }

    /// <summary>
    /// Tastenwiederholung beim Halten (etwa die Rücktaste) wie in Windows-Eingabefeldern: einmal sofort, nach einer kurzen
    /// Verzögerung dann im festen Takt, solange die Taste gehalten wird.
    /// </summary>
    public struct TastenWiederholung
    {
        private readonly float _verzoegerung, _abstand;
        private float _naechste;
        private bool _laeuft;

        public TastenWiederholung(float verzoegerung, float abstand)
        {
            _verzoegerung = Math.Max(0f, verzoegerung);
            _abstand = Math.Max(0.01f, abstand);
            _naechste = 0f;
            _laeuft = false;
        }

        /// <summary>Wie oft die Aktion in diesem Bild auszuführen ist: 0 oder 1. Nach einem Hänger wird nicht nachgeholt.</summary>
        /// <param name="gedrueckt">Taste in diesem Bild neu gedrückt.</param>
        /// <param name="gehalten">Taste wird gerade gehalten.</param>
        /// <param name="jetzt">Zeit in Sekunden (unskaliert).</param>
        public int Schritte(bool gedrueckt, bool gehalten, float jetzt)
        {
            if (gedrueckt)
            {
                _laeuft = true;
                _naechste = jetzt + _verzoegerung;
                return 1;
            }
            if (!gehalten || !_laeuft)
            {
                _laeuft = false;
                return 0;
            }
            if (jetzt < _naechste) return 0;
            _naechste = jetzt - _naechste > _abstand ? jetzt + _abstand : _naechste + _abstand;
            return 1;
        }
    }
}
