using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class HIZRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class RenderObjectsSettings
    {
        
    }
    

    // public RenderObjectsSettings settings = new RenderObjectsSettings();

    private HIZRenderPass renderObjectsPass;

    public override void Create()
    {
        renderObjectsPass = new HIZRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (null == DrawCubes.instance) return;
        
        if (DrawCubes.instance.cullingType == DrawCubes.CullingType.ComputeHIZCulling)
        {
            renderObjectsPass.SetUp(DrawCubes.instance.mesh, DrawCubes.instance.mat, DrawCubes.instance.hizComputeShader,
                DrawCubes.instance.localToWorldMatrixs);
            if (DrawCubes.instance.localToWorldMatrixs.Count > 0)
                renderer.EnqueuePass(renderObjectsPass);
        }
        
    }


    /// <summary>
    /// Render all objects that have a 'DepthOnly' pass into the given depth buffer.
    ///
    /// You can use this pass to prime a depth buffer for subsequent rendering.
    /// Use it as a z-prepass, or use it to generate a depth buffer.
    /// </summary>
    public class HIZRenderPass : ScriptableRenderPass
    {

        const string m_ProfilerTag = "HIZRenderPass";
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        private ComputeShader computeShader;
        private int kernelId;

        private ComputeBuffer cullResult;
        private uint[] args = new uint[5] {0, 0, 0, 0, 0};
        private ComputeBuffer argsBuffer;
        private Material mat;
        private Mesh mesh;
        
        private ComputeBuffer localToWorldMatrixBuffer;
        private Vector4[] planes = new Vector4[6];
        private int instanceCount;

        public void SetUp(Mesh mesh, Material mat, ComputeShader computeShader, List<Matrix4x4> localToWorldMatrixs)
        {
            this.mat = mat;
            this.mesh = mesh;
            this.computeShader = computeShader;
            this.instanceCount = localToWorldMatrixs.Count;
            kernelId = computeShader.FindKernel("CSMain");
            cullResult?.Release();
            cullResult = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16, ComputeBufferType.Append);
            localToWorldMatrixBuffer?.Release();
            localToWorldMatrixBuffer = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16);
            localToWorldMatrixBuffer.SetData(localToWorldMatrixs);
            computeShader.SetInt("instanceCount", instanceCount);
            args[0] = (uint) mesh.GetIndexCount(0);
            args[1] = 0;
            args[2] = (uint) mesh.GetIndexStart(0);
            args[3] = (uint) mesh.GetBaseVertex(0);
            argsBuffer = new ComputeBuffer(args.Length,  sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        /// <summary>
        /// Create the DepthOnlyPass
        /// </summary>
        public HIZRenderPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                
                Plane[] _planes = new Plane[6];
                GeometryUtility.CalculateFrustumPlanes(Camera.main.cullingMatrix, _planes);
                
                var m = GL.GetGPUProjectionMatrix( DrawCubes.instance.Camera.projectionMatrix,false) *  DrawCubes.instance.Camera.worldToCameraMatrix;

                //高版本 可用  computeShader.SetMatrix("matrix_VP", m); 代替 下面数组传入
                float[] mlist = new float[] {
                    m.m00,m.m10,m.m20,m.m30,
                    m.m01,m.m11,m.m21,m.m31,
                    m.m02,m.m12,m.m22,m.m32,
                    m.m03,m.m13,m.m23,m.m33
                };


                // shader.SetFloats("matrix_VP", mlist);
                
                for (int i = 0; i < _planes.Length; i++)
                {
                    planes[i] = new Vector4(_planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
                }
                argsBuffer.SetData(args);
                cmd.SetComputeFloatParams(computeShader, "matrix_VP", mlist);
                cmd.SetComputeIntParam(computeShader, "instanceCount", instanceCount);
                cmd.SetComputeVectorArrayParam(computeShader,"Planes", planes);
                cmd.SetComputeTextureParam(computeShader, kernelId, "_HIZDepth", Shader.PropertyToID("_HIZDepth"));
                cmd.SetComputeBufferParam(computeShader, kernelId,"localToWorldMatrixs", localToWorldMatrixBuffer);
                cullResult.SetCounterValue(0);
                cmd.SetComputeBufferParam(computeShader, kernelId,"positionBuffer", cullResult);
                cmd.SetComputeBufferParam(computeShader, kernelId,"argsBuffer", argsBuffer);
                cmd.DispatchCompute(computeShader, kernelId, instanceCount / 640 + 1, 1, 1);
                // uint[] countBufferData = new uint[1] {0};
                // var countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.IndirectArguments);
                // cmd.CopyCounterValue(cullResult, countBuffer, 0);
                // countBuffer.GetData(countBufferData);
                mat.SetBuffer("positionBuffer", cullResult);
                // args[1] = 10000;
                // Debug.Log(countBufferData[0]);
                // cmd.SetBufferData(argsBuffer, args);

                cmd.DrawMeshInstancedIndirect(mesh, 0, mat, 0, argsBuffer);
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();


            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

    }
}