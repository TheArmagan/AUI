// <rawimage mask="circle">: UI-Default with the graphic's alpha multiplied by a circle.
//
// WHY A SHADER AND NOT A Mask / RectMask2D.
// A Mask makes uGUI instantiate a stencil copy of every masked graphic's material, one per
// graphic per rebuild, and a RectMask2D only clips to rectangles. The worlds this package ships
// into ban both for exactly that cost. A circle is arithmetic on the fragment's position inside
// its own quad, so it costs nothing but this material.
//
// WHY _AuiUvRect.
// RawImage writes its uvRect straight into the vertex UVs: with uv="{r}" picking one tile of a
// sheet, TEXCOORD0 runs over that tile, not over 0..1. A circle computed from TEXCOORD0 would
// follow the crop and land off-centre (or vanish) on every tile but the whole texture. RawImage
// fills no second UV channel and the vertex colour is 8 bits, too coarse to carry a tile rect,
// so the rect comes in as a material property: AuiView.SetRawUv pushes it into this element's
// own material (the builder bakes one per element and per repeat row; see REFERENCE section 11)
// and the vertex stage turns TEXCOORD0 back into the quad's own 0..1 with it. That mapping is
// affine, so interpolating the result is exact.
//
// IT IS DERIVED FROM UI-Default AND KEEPS ALL OF ITS PLUMBING. The stencil block, ColorMask and
// the UNITY_UI_CLIP_RECT / UNITY_UI_ALPHACLIP keywords are what let a UI shader sit under a
// nested canvas, a RectMask2D a world may still use elsewhere, and the canvas's own ZTest.
//
// A NON-SQUARE ELEMENT GETS AN ELLIPSE: the circle has radius 0.5 in quad space, whatever the
// quad's aspect. Give the element equal width and height for a circle.
//
// QUEST: one texture read, no branches, no grab pass. fwidth needs target 3.0, which every
// VRChat platform has; it gives the edge one pixel of anti-aliasing at any distance, where a
// fixed feather would be a blur up close and a jagged edge across the room.
Shader "AUI/CircleMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // RawImage.uvRect as (x, y, width, height). Written by AuiView.SetRawUv; (0, 0, 1, 1) is
        // the whole texture, which is also what an element that never sets uv shows.
        _AuiUvRect ("UV Rect (set by AuiView)", Vector) = (0,0,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                // The fragment's place in the element's own quad, 0..1 on both axes, whatever
                // part of the texture the quad shows.
                float2 quad          : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float4 _AuiUvRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;

                // Undo RawImage's uvRect. A zero-sized rect would divide by zero and paint NaN
                // over the canvas; it is held at a hair above zero instead (a flipped rect has a
                // negative size and is fine: the quad still maps back to 0..1).
                float2 size = _AuiUvRect.zw;
                size = (abs(size) < 1e-6) ? float2(1e-6, 1e-6) : size;
                OUT.quad = (v.texcoord - _AuiUvRect.xy) / size;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // Distance from the quad's centre against a radius of 0.5, softened over one
                // screen pixel. saturate((r - d) / aa + 0.5) is 0.5 exactly on the rim, so the
                // circle neither grows nor shrinks with distance.
                float d = length(IN.quad - 0.5);
                float aa = max(fwidth(d), 1e-5);
                color.a *= saturate((0.5 - d) / aa + 0.5);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
