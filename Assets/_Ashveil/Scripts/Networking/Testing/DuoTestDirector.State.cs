using Ashveil.Gameplay.Enemies;
using Ashveil.Rendering.Sky;
using UnityEngine;

namespace Ashveil.Networking
{
    public sealed partial class DuoTestDirector
    {
        private SuiteOverrides _previousSuiteOverrides;

        // Test-only static knobs must survive normal suite resets, early exits and coroutine cancellation.
        // Capture entry values rather than assuming that the caller used production defaults.
        private struct SuiteOverrides
        {
            private int _seed;
            private float _timeScale, _startDelay, _bellCountdown, _wipeDelay, _reset, _idle;
            private bool _ignoreLocal;
            private Vector3? _viewer;
            private float _charge, _flood, _spit, _eater, _broadside;

            public static SuiteOverrides Capture() => new SuiteOverrides
            {
                _seed = ColosseumInstanceHandler.SeedOverride,
                _timeScale = ColosseumInstanceHandler.TimeScaleOverride,
                _startDelay = ColosseumInstanceHandler.StartDelayOverride,
                _bellCountdown = ColosseumInstanceHandler.BellCountdownOverride,
                _wipeDelay = ColosseumInstanceHandler.WipeDelayOverride,
                _reset = DungeonDirector.ResetOverride,
                _idle = EnemyDirector.TestIdleExtraTimeout,
                _ignoreLocal = SkyDirector.IgnoreLocal,
                _viewer = SkyDirector.ViewerOverride,
                _charge = ForemanEncounter.FirstChargeOverride,
                _flood = SaltMotherEncounter.FirstFloodOverride,
                _spit = SaltMotherEncounter.FirstSpitOverride,
                _eater = HeartEncounter.FirstEaterOverride,
                _broadside = CaptainEncounter.FirstBroadsideOverride
            };

            public void Restore()
            {
                ColosseumInstanceHandler.SeedOverride = _seed;
                ColosseumInstanceHandler.TimeScaleOverride = _timeScale;
                ColosseumInstanceHandler.StartDelayOverride = _startDelay;
                ColosseumInstanceHandler.BellCountdownOverride = _bellCountdown;
                ColosseumInstanceHandler.WipeDelayOverride = _wipeDelay;
                DungeonDirector.ResetOverride = _reset;
                EnemyDirector.TestIdleExtraTimeout = _idle;
                SkyDirector.IgnoreLocal = _ignoreLocal;
                SkyDirector.ViewerOverride = _viewer;
                ForemanEncounter.FirstChargeOverride = _charge;
                SaltMotherEncounter.FirstFloodOverride = _flood;
                SaltMotherEncounter.FirstSpitOverride = _spit;
                HeartEncounter.FirstEaterOverride = _eater;
                CaptainEncounter.FirstBroadsideOverride = _broadside;
            }
        }
    }
}
