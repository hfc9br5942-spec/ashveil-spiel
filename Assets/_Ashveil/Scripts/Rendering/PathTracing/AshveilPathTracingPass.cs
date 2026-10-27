using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Render-Graph-Anbindung (§7): Vor der Nachbearbeitung ersetzt das Pathtracing-Bild die Kamerafarbe; Belichtung,
    /// Tonemapping, Farbkorrektur und Bloom der normalen ASHVEIL-Pipeline laufen danach unveraendert, die UI kommt
    /// wie immer obendrauf. Die eigentliche Arbeit (RTAS-Bau, Himmel, Strahlen, Entrauschen) laeuft in einem
    /// "unsicheren" Pass mit eigenem Befehlspuffer, weil Raytracing-Befehle im Render Graph nur so erreichbar sind.
    /// </summary>
    public sealed class AshveilPathTracingPass : ScriptableRenderPass
    {
        private readonly AshveilPathTracer _tracer;
        private readonly ProfilingSampler _sampler;

        private sealed class PassData
        {
            public AshveilPathTracer Tracer;
            public TextureHandle Destination;
            public TextureHandle Source;
            public int Width, Height;
        }

        public AshveilPathTracingPass(AshveilPathTracer tracer, ProfilingSampler sampler)
        {
            _tracer = tracer;
            _sampler = sampler;
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_AshveilPathTraceColor";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddUnsafePass<PassData>("Ashveil Pathtracing", out PassData data, _sampler))
            {
                data.Tracer = _tracer;
                data.Destination = destination;
                data.Source = source;
                data.Width = desc.width;
                data.Height = desc.height;
                builder.UseTexture(destination, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);   // Rasterbild fuer den Belichtungsabgleich
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    d.Tracer.Execute(cmd, d.Width, d.Height, d.Destination, d.Source);
                });
            }
            resources.cameraColor = destination;
        }
    }
}
