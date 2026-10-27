using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Vollbild-Pass fuer den Ashveil-Hoehennebel (Render Graph, URP 17): liest Farbe und Tiefe, rechnet
    /// exponentiellen Hoehennebel mit Sonnenstreuung und schreibt das Ergebnis als neue Kamerafarbe.
    /// Laeuft nach Opakem und Himmel, vor Transparentem (Partikel bleiben klar).
    /// </summary>
    public sealed class AtmosphereFogPass : ScriptableRenderPass
    {
        private Material _material;

        private sealed class PassData
        {
            public TextureHandle Source;
            public Material Material;
        }

        public AtmosphereFogPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(Material material) => _material = material;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_AshveilFogTarget";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ashveil Atmosphere Fog", out PassData data))
            {
                data.Source = source;
                data.Material = _material;
                builder.UseTexture(source);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
            }
            resources.cameraColor = destination;
        }
    }
}
