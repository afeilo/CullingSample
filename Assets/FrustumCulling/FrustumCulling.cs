using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 利用jobs视锥体剔除的cs类
/// </summary>
public class FrustumCulling
{
    private static NativeArray<float4> _frustumPlanes;

    public static void SetFrustumArray(float4x4 worldProjectionMatrix)
    {
        if (_frustumPlanes.IsCreated == false)
        {
            _frustumPlanes = new NativeArray<float4>(6, Allocator.Persistent);
        }
        Plane[] _planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(worldProjectionMatrix, _planes);
        for (int i = 0; i < _planes.Length; i++)
        {
            _frustumPlanes[i] = new float4(_planes[i].normal, _planes[i].distance);
        }
    }

    private struct FrustumViewFilter : IJobParallelForFilter
    {
        [ReadOnly] public NativeArray<float4> FrustumPlanes;
        [ReadOnly] public NativeArray<float3> Positions;
        public bool Execute(int index)
        {
            for (int i = 0; i < FrustumPlanes.Length; i++)
            {
                var normal = FrustumPlanes[i].xyz;
                var distance = FrustumPlanes[i].w;
                if (math.dot(normal, Positions[index]) + distance <= 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public static JobHandle ScheduleCullingJob(NativeArray<float3> Positions, NativeList<int> outIndices)
    {
        if (!_frustumPlanes.IsCreated)
        {
            return default(JobHandle);
        }

        return new FrustumViewFilter
        {
            FrustumPlanes = _frustumPlanes,
            Positions = Positions,
        }.ScheduleAppend(outIndices, Positions.Length, 8);
    }

    public static void Dispose()
    {
        if (_frustumPlanes.IsCreated)
        {
            _frustumPlanes.Dispose();
        }

    }
}
