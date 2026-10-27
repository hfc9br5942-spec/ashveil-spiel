using Ashveil.Rendering;
using Ashveil.World;

namespace Ashveil.Networking
{
    public sealed partial class DuoTestDirector
    {
        private bool _ownsIndoorTest, _previousIndoorOverride, _previousIndoorRoom;
        private DungeonSlot _previousIndoorSlot;

        private void BeginIndoorTest()
        {
            if (_ownsIndoorTest) return;
            _previousIndoorOverride = AtmosphereController.IndoorOverride;
            _previousIndoorRoom = IndoorAtmosphere.IsIndoors;
            _previousIndoorSlot = IndoorAtmosphere.CurrentSlot;
            _ownsIndoorTest = true;
            AtmosphereController.IndoorOverride = true;
        }

        private void RestoreIndoorTest()
        {
            if (!_ownsIndoorTest) return;
            _ownsIndoorTest = false;
            // A real Enter/Leave or an external flag reset takes precedence over this short synthetic test window.
            if (IndoorAtmosphere.IsIndoors == _previousIndoorRoom &&
                IndoorAtmosphere.CurrentSlot == _previousIndoorSlot && AtmosphereController.IndoorOverride)
                AtmosphereController.IndoorOverride = _previousIndoorOverride;
            _previousIndoorSlot = null;
        }
    }
}
