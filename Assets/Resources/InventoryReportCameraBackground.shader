Shader "AR Inventory/Camera Background"
{
    Properties { _MainTex ("Camera", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(appdata_img input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target { return tex2D(_MainTex, input.uv); }
            ENDCG
        }
    }
}
