using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Bindepunkt der Szene (Kareth v4, Kanon 05.10.: „Rückkehr- und Bindepunkt bleibt das Wachtfeuer“, Start an der
    /// Ankunftsstelle). Hier wacht man nach dem Tod auf, hierher kommt man nach einem Sturz aus der Welt, und hier beginnt ein
    /// bekannter Charakter, dessen gespeicherte Stelle in dieser Szene nicht gilt (<see cref="ScenePositionKey"/>). Neue
    /// Charaktere beginnen weiter am Spawn. Ohne Bindepunkt (Sandbox) gilt wie bisher der Spawn; ein Wiederbelebungspunkt der
    /// Dungeon-Instanz geht immer vor (NetworkMotion.ServerSetRespawnPoint).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BindPoint : MonoBehaviour
    {
        /// <summary>Bindepunkt der geladenen Szene (null = keiner).</summary>
        public static BindPoint Current { get; private set; }

        public Vector3 Position => transform.position;
        public float Yaw => transform.eulerAngles.y;

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.85f);
            Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward * 1.5f);
        }
    }
}
