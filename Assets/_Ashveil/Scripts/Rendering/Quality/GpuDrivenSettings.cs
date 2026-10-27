using System;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Rendering-Bibel §21/§22: GPU Resident Drawer und GPU-Occlusion-Culling zur Laufzeit lesen und umschalten.
    /// Heute nur fuer den GPU-Driven-Vergleich (RenderBaseline -ashveil-renderbench-gpu); spaeter der Haken,
    /// ueber den die Qualitaetsstufen V2 Occlusion je Stufe/Region setzen. Im Editor aendert das Umschalten das
    /// URP-Asset selbst – Aufrufer stellen den Ausgangswert wieder her.
    /// </summary>
    public static class GpuDrivenSettings
    {
        private static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        public static bool Available => Asset != null;

        // Der Enum-Typ liegt in Unity.RenderPipelines.GPUDriven.Runtime – ueber Reflection, damit die Rendering-Assembly
        // keine weitere Referenz braucht (0 = Disabled, 1 = InstancedDrawing).
        private static readonly PropertyInfo ModeProperty =
            typeof(UniversalRenderPipelineAsset).GetProperty("gpuResidentDrawerMode", BindingFlags.Instance | BindingFlags.Public);

        public static bool DrawerEnabled
        {
            get => Asset != null && ModeProperty != null && Convert.ToInt32(ModeProperty.GetValue(Asset)) == 1;
            set
            {
                if (Asset == null || ModeProperty == null) return;
                ModeProperty.SetValue(Asset, Enum.ToObject(ModeProperty.PropertyType, value ? 1 : 0));
            }
        }

        public static bool OcclusionEnabled
        {
            get => Asset != null && Asset.gpuResidentDrawerEnableOcclusionCullingInCameras;
            set { if (Asset != null) Asset.gpuResidentDrawerEnableOcclusionCullingInCameras = value; }
        }
    }
}
