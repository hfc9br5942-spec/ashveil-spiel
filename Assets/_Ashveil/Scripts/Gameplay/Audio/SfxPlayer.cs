using UnityEngine;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>
    /// Spielt kurze 3D-Klaenge ueber einen kleinen Pool von AudioSources (keine Allokation pro Schuss).
    /// </summary>
    public static class SfxPlayer
    {
        private const int PoolSize = 16;
        private static AudioSource[] _pool;
        private static int _next;

        public static void Play(SfxId id, Vector3 position, float volume = 1f, float pitchVariation = 0.08f)
        {
            Play(ProceduralSfx.Get(id), position, volume, pitchVariation);
        }

        /// <summary>Spielt clip, oder den Platzhalter fallback, falls clip leer ist.</summary>
        public static void Play(AudioClip clip, SfxId fallback, Vector3 position, float volume = 1f, float pitchVariation = 0.08f)
        {
            Play(clip != null ? clip : ProceduralSfx.Get(fallback), position, volume, pitchVariation);
        }

        public static void Play(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariation = 0.08f)
            => Play(clip, position, volume, pitchVariation, 60f);

        /// <summary>Weit hoerbarer Klang (Glocken, Stampfer eines Kolosses).</summary>
        public static void PlayFar(SfxId id, Vector3 position, float volume, float maxDistance, float pitchVariation = 0.04f)
            => Play(ProceduralSfx.Get(id), position, volume, pitchVariation, maxDistance);

        private static void Play(AudioClip clip, Vector3 position, float volume, float pitchVariation, float maxDistance)
        {
            if (clip == null) return;
            EnsurePool();
            AudioSource src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            if (src == null) return;

            src.transform.position = position;
            src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            src.volume = volume;
            src.maxDistance = Mathf.Max(src.minDistance + 1f, maxDistance);
            src.clip = clip;
            src.Play();
        }

        private static void EnsurePool()
        {
            if (_pool != null && _pool[0] != null) return;
            var root = new GameObject("SfxPool") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(root);
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx " + i);
                go.transform.SetParent(root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0.75f; // ueberwiegend 3D, eigene Waffen bleiben gut hoerbar
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                src.maxDistance = 60f;
                src.dopplerLevel = 0f;
                _pool[i] = src;
            }
            _next = 0;
        }
    }
}
