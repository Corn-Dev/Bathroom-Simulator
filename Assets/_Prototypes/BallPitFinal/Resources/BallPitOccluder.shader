// Depth-only occluder pass: draws last frame's visible balls from THIS frame's camera into an RFloat
// target holding linear eye depth. Uses its own matrices, so it does not depend on URP camera state.
Shader "Hidden/BallPitFinal/Occluder"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            struct BallInstance { float3 pos; uint packed; };
            StructuredBuffer<BallInstance> _Instances;
            StructuredBuffer<uint> _VisibleIds;
            float4x4 _OccViewProj; // GL.GetGPUProjectionMatrix(proj, true) * view
            float4x4 _OccView;     // view
            float _BallRadius;

            struct Attributes { float4 positionOS : POSITION; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float eye : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                uint id = _VisibleIds[v.instanceID];
                BallInstance b = _Instances[id];
                if ((b.packed & 256u) == 0u) { o.positionCS = float4(0, 0, -2, 1); o.eye = 0; return o; }
                float3 p = b.pos + v.positionOS.xyz * (_BallRadius * 2.0);
                o.positionCS = mul(_OccViewProj, float4(p, 1.0));
                o.eye = -mul(_OccView, float4(p, 1.0)).z;
                return o;
            }

            float Frag(Varyings i) : SV_Target { return i.eye; }
            ENDHLSL
        }
    }
}
