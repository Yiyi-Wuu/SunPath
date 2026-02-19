// ShadowCapture.shader
Shader "Custom/ShadowCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 方向光方向（假设主光源是 Directional Light）
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0, dot(i.worldNormal, lightDir));

                // 可选：加上阴影（需要开启阴影投射）
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float shadow = atten; // 0=全阴影，1=无阴影

                // 输出阴影程度：1 - shadow 表示“越暗值越大”
                return fixed4(1 - shadow, 0, 0, 1);
            }
            ENDCG
        }
    }
}