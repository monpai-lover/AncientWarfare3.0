Shader "AW3/HierarchicalVassal/Fill"
{
    Properties
    {
        _MainTex ("Fill Texture", 2D) = "white" {}
        _OverlayAlpha ("Overlay Alpha", Range(0, 1)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.001, 1)) = 0.08
        _HeightTex ("Height", 2D) = "gray" {}
        _HeightUvScaleOffset ("Height UV Scale Offset", Vector) = (1, 1, 0, 0)
        _ReliefStrength ("Relief Strength", Range(0, 1)) = 0
        _MapLightDirection ("Map Light Direction", Vector) = (0, 0, 1, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _OverlayAlpha;
            half _EdgeSoftness;
            sampler2D _HeightTex;
            float4 _HeightTex_TexelSize;
            float4 _HeightUvScaleOffset;
            half _ReliefStrength;
            float4 _MapLightDirection;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR0;
                float2 uv : TEXCOORD0;
                float2 heightUv : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.heightUv = input.uv * _HeightUvScaleOffset.xy +
                    _HeightUvScaleOffset.zw;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * input.color;

                // Centered differences keep a flat height field neutral and
                // intentionally affect lighting only, never vertex position.
                float2 texel = _HeightTex_TexelSize.xy;
                float heightLeft = tex2D(_HeightTex,
                    input.heightUv - float2(texel.x, 0)).r;
                float heightRight = tex2D(_HeightTex,
                    input.heightUv + float2(texel.x, 0)).r;
                float heightDown = tex2D(_HeightTex,
                    input.heightUv - float2(0, texel.y)).r;
                float heightUp = tex2D(_HeightTex,
                    input.heightUv + float2(0, texel.y)).r;
                float heightPlus = heightRight - heightLeft;
                float heightMinus = heightUp - heightDown;
                float3 normal = normalize(float3(
                    -heightPlus * _ReliefStrength,
                    -heightMinus * _ReliefStrength, 1));
                float3 mapLight = normalize(_MapLightDirection.xyz);
                float light = saturate(dot(normal, mapLight));
                float relief = lerp(1.0, 0.75 + 0.25 * light,
                    saturate(_ReliefStrength) *
                    saturate(length(float2(heightPlus, heightMinus)) * 4));
                color.rgb *= relief;
                float alphaDerivative = max(fwidth(input.color.a),
                    _EdgeSoftness * 0.001);
                float edgeFeather = smoothstep(0.0,
                    max(alphaDerivative, _EdgeSoftness), input.color.a);
                color.a *= edgeFeather;
                color.a *= _OverlayAlpha;
                return color;
            }
            ENDCG
        }
    }
}
