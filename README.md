# CullingSample
- ComputeFrustumCulling：jobs + 视锥体剔除
- FrustumCulling：computeshader + 视锥体剔除
- ComputeHIZCulling：computeshader + 视锥体剔除 + HIZ剔除
- MeshClusterRender：DrawProceduralIndirect + 模型cluster划分 + computeshader + 视锥体剔除 + HIZ剔除 [参考1](https://zhuanlan.zhihu.com/p/425263243)[参考2](https://zhuanlan.zhihu.com/p/44411827)

https://user-images.githubusercontent.com/11472358/216742085-ac58e899-dcac-46a0-ba8a-b50bf1c07f03.mp4

- 下一步计划实现分Cluster带Lod的剔除方案，加入三角形剔除
