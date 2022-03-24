using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DrawDepthFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class RenderObjectsSettings
    {
        public string passTag = "RenderObjectsFeature";
        public RenderPassEvent Event;
        public LayerMask layerMask;
    }
    

    public RenderObjectsSettings settings = new RenderObjectsSettings();

    private DrawDepthPass renderObjectsPass;
    private RenderTargetHandle m_DepthTexture;
    public override void Create()
    {
        m_DepthTexture.Init("_HIZDepth");
        renderObjectsPass = new DrawDepthPass(settings.Event, settings.layerMask);

    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderObjectsPass.Setup(renderingData.cameraData.cameraTargetDescriptor, m_DepthTexture);
        renderer.EnqueuePass(renderObjectsPass);
    }


    /// <summary>
    /// Render all objects that have a 'DepthOnly' pass into the given depth buffer.
    ///
    /// You can use this pass to prime a depth buffer for subsequent rendering.
    /// Use it as a z-prepass, or use it to generate a depth buffer.
    /// </summary>
    public class DrawDepthPass : ScriptableRenderPass
    {
        int kDepthBufferBits = 32;

        private RenderTargetHandle depthAttachmentHandle { get; set; }
        internal RenderTextureDescriptor descriptor { get; private set; }

        FilteringSettings m_FilteringSettings;
        const string m_ProfilerTag = "Depth Prepass";
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);
        ShaderTagId m_ShaderTagId = new ShaderTagId("DepthOnly");

        /// <summary>
        /// Create the DepthOnlyPass
        /// </summary>
        public DrawDepthPass(RenderPassEvent evt, LayerMask layerMask)
        {
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            renderPassEvent = evt;
        }

        /// <summary>
        /// Configure the pass
        /// </summary>
        public void Setup(
            RenderTextureDescriptor baseDescriptor,
            RenderTargetHandle depthAttachmentHandle)
        {
            this.depthAttachmentHandle = depthAttachmentHandle;
            baseDescriptor.colorFormat = RenderTextureFormat.Depth;
            baseDescriptor.depthBufferBits = kDepthBufferBits;

            // Depth-Only pass don't use MSAA
            baseDescriptor.msaaSamples = 1;
            descriptor = baseDescriptor;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cmd.GetTemporaryRT(depthAttachmentHandle.id, descriptor, FilterMode.Point);
            ConfigureTarget(depthAttachmentHandle.Identifier());
            ConfigureClear(ClearFlag.All, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;
                
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

            if (depthAttachmentHandle != RenderTargetHandle.CameraTarget)
            {
                cmd.ReleaseTemporaryRT(depthAttachmentHandle.id);
                depthAttachmentHandle = RenderTargetHandle.CameraTarget;
            }
        }
    }
}