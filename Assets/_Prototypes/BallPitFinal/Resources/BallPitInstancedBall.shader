// URP 14 procedural-instanced ball. Reads ball centers/colors from structured buffers (no per-instance matrices).
// Per-pixel sphere normal from object-space position, so the low-poly mesh shades like a perfect sphere.
Shader "BallPitFinal/InstancedBall"
{
    Properties
    {
        _Gloss("Gloss", Range(4, 256)) = 48
        _SpecStrength("Spec Strength", Range(0, 1)) = 0.3
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct BallInstance { float3 pos; uint packed; };
        StructuredBuffer<BallInstance> _Instances;
        StructuredBuffer<uint> _VisibleIds;
        float _BallRadius;
        float4 _Palette[16];

        CBUFFER_START(UnityPerMaterial)
            float _Gloss;
            float _SpecStrength;
            float _AmbientStrength;
        CBUFFER_END

        bool FetchBall(uint instanceID, out float3 center, out uint packed)
        {
            uint id = _VisibleIds[instanceID];
            BallInstance b = _Instances[id];
            center = b.pos;
            packed = b.packed;
            return (b.packed & 256u) != 0u;
        }

        struct BallAttributes { float4 positionOS : POSITION; uint instanceID : SV_InstanceID; };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                half3  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings Vert(BallAttributes v)
            {
                Varyings o = (Varyings)0;
                float3 c; uint packed;
                if (!FetchBall(v.instanceID, c, packed)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                float3 posWS = c + v.positionOS.xyz * (_BallRadius * 2.0);
                o.positionWS = posWS;
                o.normalOS = v.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(posWS);
                o.color = (half3)_Palette[packed & 15u].rgb;
                o.fogFactor = (half)ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalOS); // no rotation, so object-space direction == world normal
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 albedo = i.color;
                half atten = light.shadowAttenuation * light.distanceAttenuation;
                half3 direct = albedo * light.color * (saturate(dot(n, light.direction)) * atten);
                half3 ambient = albedo * SampleSH(n) * _AmbientStrength;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 h = normalize(light.direction + viewDir);
                half3 spec = light.color * (pow(saturate(dot(n, h)), _Gloss) * _SpecStrength * atten);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambient *= ao.indirectAmbientOcclusion;
                    direct *= ao.directAmbientOcclusion;
                #endif
                half3 col = MixFog(direct + ambient + spec, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // Shadows.hlsl uses LerpWhiteTo (from CommonMaterial.hlsl via Core's
            // common includes). Core.hlsl in the shared HLSLINCLUDE doesn't always
            // pull it into this pass's scope, so include it explicitly first.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings { float4 positionCS : SV_POSITION; };

            ShadowVaryings ShadowVert(BallAttributes v)
            {
                ShadowVaryings o;
                float3 c; uint packed;
                if (!FetchBall(v.instanceID, c, packed)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                float3 posWS = c + v.positionOS.xyz * (_BallRadius * 2.0);
                float3 nWS = normalize(v.positionOS.xyz);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings { float4 positionCS : SV_POSITION; };

            DepthVaryings DepthVert(BallAttributes v)
            {
                DepthVaryings o;
                float3 c; uint packed;
                if (!FetchBall(v.instanceID, c, packed)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                o.positionCS = TransformWorldToHClip(c + v.positionOS.xyz * (_BallRadius * 2.0));
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct DNVaryings { float4 positionCS : SV_POSITION; float3 normalOS : TEXCOORD0; };

            DNVaryings DNVert(BallAttributes v)
            {
                DNVaryings o = (DNVaryings)0;
                float3 c; uint packed;
                if (!FetchBall(v.instanceID, c, packed)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                o.positionCS = TransformWorldToHClip(c + v.positionOS.xyz * (_BallRadius * 2.0));
                o.normalOS = v.positionOS.xyz;
                return o;
            }

            half4 DNFrag(DNVaryings i) : SV_Target
            {
                float3 n = normalize(i.normalOS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 rem = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(rem), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(n), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
