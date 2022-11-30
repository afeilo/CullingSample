using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public class DrawCubes : MonoBehaviour
{
    public int instanceCount;
    public Material mat;
    public Mesh mesh;
    public ComputeShader hizComputeShader;
    public ComputeShader computeShader;
    public CullingType cullingType = CullingType.jobsFrustumCulling;
    [HideInInspector] public List<Matrix4x4> localToWorldMatrixs;
    [HideInInspector] public Camera Camera;

    public static DrawCubes instance;

    void Awake()
    {
        instance = this;
        Camera = Camera.main;
        localToWorldMatrixs = new List<Matrix4x4>();
        //周围一圈随机生成
        for (int i = 0; i < instanceCount; i++)
        {
            float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
            float distance = Random.Range(20.0f, 100.0f);
            float height = Random.Range(-2.0f, 2.0f);
            float size = Random.Range(0.05f, 0.25f);
            Vector4 position = new Vector4(Mathf.Sin(angle) * distance, height, Mathf.Cos(angle) * distance, size);
            localToWorldMatrixs.Add(Matrix4x4.TRS(position, Quaternion.identity, new Vector3(size, size, size)));
        }
    }

    void OnEnable()
    {
        switch (cullingType)
        {
            case CullingType.jobsFrustumCulling:
                InitArray();
                break;
            case CullingType.ComputeFrustumCulling:
                InitComputeData();
                break;
            case CullingType.ComputeHIZCulling:
                InitHizCompute();
                break;
        }
    }

    // Update is called once per frame
    void Update()
    {
        switch (cullingType)
        {
            case CullingType.jobsFrustumCulling:
                CreateJobs();
                break;
            case CullingType.ComputeFrustumCulling:
                UpdateComputeFrustumCulling();
                break;
            case CullingType.ComputeHIZCulling:
                UpdateComputeHIZCulling();
                break;
        }
    }

    void LateUpdate()
    {
        if (cullingType == CullingType.jobsFrustumCulling)
        {
            CompleteJobs();
            Graphics.DrawMeshInstancedIndirect(mesh, 0, mat,
                new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
        }
    }

    void OnDisable()
    {
        argsBuffer?.Dispose();
        cullResult?.Dispose();
        FrustumCulling.Dispose();
    }

    #region 实例化

    void CreateInstance()
    {
        foreach (var matrix in localToWorldMatrixs)
        {
            GameObject go = new GameObject();
            go.transform.parent = transform;
            go.transform.position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
            go.transform.localRotation = matrix.rotation;
            go.transform.localScale = matrix.lossyScale;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
        }
    }

    void ClearInstance()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject.DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    #endregion

    #region JobSystem

    private NativeArray<float3> Positions;
    private NativeList<int> Indices;
    private JobHandle jobHandle;
    ComputeBuffer cullResult;
    uint[] args = new uint[5] {0, 0, 0, 0, 0};
    ComputeBuffer argsBuffer;
    private List<float4x4> cullLocalToWorldMatrixs;
    private bool isCompleted = true;

    void InitArray()
    {
        Positions = new NativeArray<float3>(localToWorldMatrixs.Count, Allocator.Persistent);
        for (int i = 0; i < localToWorldMatrixs.Count; i++)
        {
            var matrix = localToWorldMatrixs[i];
            Positions[i] = new float3(matrix.m03, matrix.m13, matrix.m23);
        }


        cullLocalToWorldMatrixs = new List<float4x4>();
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void CreateJobs()
    {
        // if (!isCompleted) return;
        FrustumCulling.SetFrustumArray(Camera.main.cullingMatrix);
        Indices = new NativeList<int>(Allocator.TempJob);
        jobHandle = FrustumCulling.ScheduleCullingJob(Positions, Indices);
        isCompleted = false;
    }

    void CompleteJobs()
    {
        // if (!jobHandle.IsCompleted) return;
        // isCompleted = true;
        jobHandle.Complete();
        if (Indices.Length <= 0)
        {
            Indices.Dispose();
            return;
        }

        Profiler.BeginSample("111");
        cullLocalToWorldMatrixs.Clear();
        for (int i = 0; i < Indices.Length; i++)
        {
            cullLocalToWorldMatrixs.Add(localToWorldMatrixs[Indices[i]]);
        }

        Profiler.EndSample();

        if (null != cullResult)
            cullResult.Release();
        cullResult = new ComputeBuffer(Indices.Length, 4 * 16);
        cullResult.SetData(cullLocalToWorldMatrixs);
        mat.SetBuffer("positionBuffer", cullResult);
        if (mesh != null)
        {
            args[0] = (uint) mesh.GetIndexCount(0);
            args[1] = (uint) Indices.Length;
            args[2] = (uint) mesh.GetIndexStart(0);
            args[3] = (uint) mesh.GetBaseVertex(0);
        }
        else
        {
            args[0] = args[1] = args[2] = args[3] = 0;
        }

        argsBuffer.SetData(args);

        Indices.Dispose();
    }

    #endregion

    private int kernelId;
    private ComputeBuffer localToWorldMatrixBuffer;

    #region ComputeShader

    private Vector4[] planes = new Vector4[6];

    void InitComputeData()
    {
        kernelId = computeShader.FindKernel("CSMain");
        cullResult?.Release();
        cullResult = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16, ComputeBufferType.Append);
        localToWorldMatrixBuffer?.Release();
        localToWorldMatrixBuffer = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16);
        localToWorldMatrixBuffer.SetData(localToWorldMatrixs);
        computeShader.SetInt("instanceCount", instanceCount);
        args[0] = (uint) mesh.GetIndexCount(0);
        args[2] = (uint) mesh.GetIndexStart(0);
        args[3] = (uint) mesh.GetBaseVertex(0);
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void UpdateComputeFrustumCulling()
    {
        Plane[] _planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(Camera.main.cullingMatrix, _planes);
        for (int i = 0; i < _planes.Length; i++)
        {
            planes[i] = new Vector4(_planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
        }

        cullResult.SetCounterValue(0);
        computeShader.SetVectorArray("Planes", planes);
        computeShader.SetBuffer(kernelId, "localToWorldMatrixs", localToWorldMatrixBuffer);
        computeShader.SetBuffer(kernelId, "positionBuffer", cullResult);
        computeShader.Dispatch(kernelId, instanceCount / 640 + 1, 1, 1);

        uint[] countBufferData = new uint[1] {0};
        var countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.IndirectArguments);
        ComputeBuffer.CopyCount(cullResult, countBuffer, 0);
        countBuffer.GetData(countBufferData);

        mat.SetBuffer("positionBuffer", cullResult);
        args[1] = countBufferData[0];
        argsBuffer.SetData(args);
        Graphics.DrawMeshInstancedIndirect(mesh, 0, mat,
            new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)), argsBuffer);
    }

    #endregion

    #region ComputeHIZCulling

    private CommandBuffer cmd;

    void InitHizCompute()
    {
        if (SystemInfo.usesReversedZBuffer)
        {
            hizComputeShader.EnableKeyword("_REVERSED_Z");
        }
        else
        {
            hizComputeShader.DisableKeyword("_REVERSED_Z");
        }

        this.instanceCount = localToWorldMatrixs.Count;
        kernelId = hizComputeShader.FindKernel("CSMain");
        cullResult?.Release();
        cullResult = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16, ComputeBufferType.Append);
        localToWorldMatrixBuffer?.Release();
        localToWorldMatrixBuffer = new ComputeBuffer(localToWorldMatrixs.Count, sizeof(float) * 16);
        localToWorldMatrixBuffer.SetData(localToWorldMatrixs);
        hizComputeShader.SetInt("instanceCount", instanceCount);
        args[0] = (uint) mesh.GetIndexCount(0);
        args[1] = 0;
        args[2] = (uint) mesh.GetIndexStart(0);
        args[3] = (uint) mesh.GetBaseVertex(0);
        argsBuffer = new ComputeBuffer(args.Length, sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void UpdateComputeHIZCulling()
    {
        cmd = CommandBufferPool.Get("HIZRenderPass");
        var m = GL.GetGPUProjectionMatrix(Camera.projectionMatrix, false) *
                Camera.worldToCameraMatrix;

        //高版本 可用  computeShader.SetMatrix("matrix_VP", m); 代替 下面数组传入
        float[] mlist = new float[]
        {
            m.m00, m.m10, m.m20, m.m30,
            m.m01, m.m11, m.m21, m.m31,
            m.m02, m.m12, m.m22, m.m32,
            m.m03, m.m13, m.m23, m.m33
        };

        argsBuffer.SetData(args);
        cmd.SetComputeFloatParams(hizComputeShader, "matrix_VP", mlist);
        cmd.SetComputeIntParam(hizComputeShader, "instanceCount", instanceCount);
        cmd.SetComputeBufferParam(hizComputeShader, kernelId, "localToWorldMatrixs", localToWorldMatrixBuffer);
        cullResult.SetCounterValue(0);
        cmd.SetComputeBufferParam(hizComputeShader, kernelId, "positionBuffer", cullResult);
        cmd.SetComputeBufferParam(hizComputeShader, kernelId, "argsBuffer", argsBuffer);
        cmd.DispatchCompute(hizComputeShader, kernelId, instanceCount / 640 + 1, 1, 1);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
        mat.SetBuffer("positionBuffer", cullResult);
        Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, new Bounds(Vector3.zero, new Vector3(100.0f, 100.0f, 100.0f)),
            argsBuffer);
    }

    #endregion

    public enum CullingType
    {
        jobsFrustumCulling,
        ComputeFrustumCulling,
        ComputeHIZCulling,
    }
}