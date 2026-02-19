 Shader "Custom/ShadowMaskForVisual"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma skip_variants INSTANCING_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                SHADOW_COORDS(1)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_LIGHT_ATTENUATION(atten, i, 0);
            // 输出：光照 = 白色，阴影 = 黑色
            return atten; // 灰度值
        }
        ENDCG
    }
    }
        FallBack Off
}