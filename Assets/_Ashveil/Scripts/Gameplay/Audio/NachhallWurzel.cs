using UnityEngine;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>
    /// Wurzel der gebackenen Nachhall-Sonden einer Szene (Claude 14.10.2026, TECH-125, Schritt 3; gebaut vom Auftrag
    /// „nachhall_backen“). Wird im Editor AUSGESCHALTET gespeichert: Steam-Audio-Sonden brauchen den Steam-Audio-Manager, den es
    /// nur am Client gibt. Die <see cref="KlangLandschaft"/> schaltet sie ein, sobald die Szene geladen ist – der Server nie.
    /// </summary>
    public sealed class NachhallWurzel : MonoBehaviour { }
}
