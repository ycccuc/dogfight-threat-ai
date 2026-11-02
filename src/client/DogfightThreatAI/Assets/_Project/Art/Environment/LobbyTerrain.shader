Shader "DogfightThreatAI/LobbyTerrain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation", Range(0,1)) = 0.4
        _Brightness ("Brightness", Range(0,1.5)) = 0.85
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment LobbyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            fixed _Saturation;
            fixed _Brightness;
            fixed4 LobbyFragment(v2f input) : SV_Target
            {
                fixed4 color = SampleSpriteTexture(input.texcoord) * input.color;
                fixed gray = dot(color.rgb, fixed3(.299, .587, .114));
                color.rgb = lerp(gray.xxx, color.rgb, _Saturation) * _Brightness;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
