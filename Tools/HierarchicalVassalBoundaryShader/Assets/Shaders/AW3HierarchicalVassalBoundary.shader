Shader "AW3/HierarchicalVassal/Boundary"
{
    Properties
    {
        _LeftColor ("Left Color", Color) = (0.25, 0.65, 1, 1)
        _RightColor ("Right Color", Color) = (1, 0.65, 0.25, 1)
        _CameraWorldPerPixel ("Camera World Per Pixel", Float) = 0.01
        _DarkOutline ("Dark Center Outline", Range(0, 1)) = 0.35
        _EdgeSoftness ("Edge Softness", Range(0.001, 1)) = 0.08
        _HeightLightWeakening ("Height Light Weakening", Range(0, 1)) = 0.2
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

            fixed4 _LeftColor;
            fixed4 _RightColor;
            float _CameraWorldPerPixel;
            half _DarkOutline;
            half _EdgeSoftness;
            half _HeightLightWeakening;
            sampler2D _HeightTex;
            float4 _HeightTex_TexelSize;
            float4 _HeightUvScaleOffset;
            half _ReliefStrength;
            float4 _MapLightDirection;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR0;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float2 heightUv : TEXCOORD2;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.heightUv = input.uv1.zw * _HeightUvScaleOffset.xy +
                    _HeightUvScaleOffset.zw;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // UV0.x is the signed distance to the centre line and UV0.y
                // carries the local half-width emitted by the ribbon builder.
                float signedDistance = input.uv0.x;
                float halfWidth = max(abs(input.uv0.y),
                    _CameraWorldPerPixel);
                float derivative = max(fwidth(signedDistance),
                    _CameraWorldPerPixel);
                float edge = max(_EdgeSoftness * halfWidth, derivative);
                float normalizedDistance = signedDistance / halfWidth;
                float side = saturate(0.5 + 0.5 * normalizedDistance);
                float tier = input.uv1.x;
                float leftWeight = smoothstep(0.0, 1.0, side);
                float rightWeight = 1.0 - leftWeight;
                fixed4 color = _LeftColor * leftWeight +
                    _RightColor * rightWeight;
                color.rgb *= lerp(1.0, 0.98, saturate(tier / 3.0));

                float center = 1.0 - smoothstep(derivative,
                    derivative + edge, abs(signedDistance));
                color.rgb *= 1.0 - center * _DarkOutline;

                float edgeAlpha = 1.0 - smoothstep(
                    halfWidth - edge, halfWidth + edge,
                    abs(signedDistance));
                float coast = input.uv1.y;
                float waterSide = step(0.5, coast) *
                    step(0.0, signedDistance);
                // Coastline water-side ribbons are intentionally subtle and
                // can become fully transparent when the mesh marks alpha 0.
                float coastAlpha = lerp(1.0, 0.0, waterSide);
                color.a *= input.color.a * edgeAlpha * coastAlpha;

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
                float3 heightNormal = normalize(float3(
                    -heightPlus * _ReliefStrength,
                    -heightMinus * _ReliefStrength, 1));
                float3 mapLight = normalize(_MapLightDirection.xyz);
                float heightLight = saturate(dot(heightNormal, mapLight));
                float relief = lerp(1.0, 0.75 + 0.25 * heightLight,
                    saturate(_ReliefStrength) *
                    saturate(length(float2(heightPlus, heightMinus)) * 4));
                float weakenedRelief = lerp(1.0, relief,
                    saturate(_HeightLightWeakening));
                color.rgb *= weakenedRelief;
                return color;
            }
            ENDCG
        }
    }
}
