// Copyright 2020 The Tilt Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

Shader "Brush/Multiplicative" {
Properties {
  _MainTex ("Texture", 2D) = "white" {}
  _Opacity ("Opacity", Range(0, 1)) = 1
}

Category {
  Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        // TODO: investigate different blend mode that allows us to remove blend in the frag
  Blend DstColor Zero
  AlphaTest Greater .01
  ColorMask RGBA
  Cull Off Lighting Off ZWrite Off Fog { Color (0,0,0,0) }

  SubShader {
    Pass {

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile __ ODS_RENDER ODS_RENDER_CM
      #include "UnityCG.cginc"
      #include "Assets/Shaders/Include/Brush.cginc"

      sampler2D _MainTex;

      struct appdata_t {
        float4 vertex : POSITION;
        fixed4 color : COLOR;
        float3 normal : NORMAL;
        float2 texcoord : TEXCOORD0;
        uint id : SV_VertexID;
      };

      struct v2f {
        float4 vertex : POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
        uint id : TEXCOORD2;
      };

      float4 _MainTex_ST;

      uniform float _ClipStart;
      uniform float _ClipEnd;
      uniform float _Opacity;

      v2f vert (appdata_t v)
      {
        PrepForOds(v.vertex);

        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
                                o.color = v.color;
        o.id = (float2)v.id;
        return o;
      }

      fixed4 frag (v2f i) : COLOR
      {
        float completion = _ClipEnd < 0 || (i.id > _ClipStart && i.id < _ClipEnd) ? 1 : -1;
        clip(completion);

         half4 c = tex2D(_MainTex, i.texcoord );
         c = i.color * c;
                                // TODO: investigate doing this in the blend mode
        c = lerp(1, c, c.a);
        c.a *= _Opacity;
        return c;
      }
      ENDCG
    }
  }
}
}
