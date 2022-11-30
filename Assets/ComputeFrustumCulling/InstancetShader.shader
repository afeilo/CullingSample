Shader "Unlit/InstancetShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
			#pragma target 4.5
            #include "UnityCG.cginc"
		#if SHADER_TARGET >= 45
			StructuredBuffer<float4x4> positionBuffer;
		#endif

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
#if SHADER_TARGET >= 45
				float4x4 data = positionBuffer[instanceID];
#else
				float4x4 data = 0;
#endif
				v2f o;
				float3 localPosition = v.vertex.xyz * data._11;
				float3 worldPosition = data._14_24_34 + localPosition;
				//float3 worldPosition = mul(data, v.vertex);
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPosition, 1.0f));
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
