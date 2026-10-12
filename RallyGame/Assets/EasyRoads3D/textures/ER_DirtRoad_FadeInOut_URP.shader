// URP port of "EasyRoads3D/ER Dirt Road Fade In - Out" (original (c) 2021 EasyRoads3D)
// Fades in / out using the vertex color alpha of the road mesh, like the original.
// Written for URP 12-17 (Unity 2021.3 - Unity 6). Not for Built-in RP or HDRP.
// Not included vs. the original: shadow casting (a flat road doesn't need it) and lightmaps (ambient comes from SH probes).

Shader "EasyRoads3D/URP/ER Dirt Road Fade In - Out"
{
	Properties
	{
		[Header(Road)]
		_MainTex("Albedo", 2D) = "white" {}
		_Color("Color", Color) = (1,1,1,1)
		_Metallic("Metallic (R) AO (G) Smoothness (A)", 2D) = "gray" {}
		_MainMetallicPower1("Metallic Power", Range(0, 2)) = 0
		_MainSmoothnessPower1("Smoothness Power", Range(0, 2)) = 1
		_OcclusionStrength1("Ambient Occlusion Power", Range(0, 2)) = 1
		_BumpMap("Normal Map", 2D) = "bump" {}
		_BumpScale("Normal Map Scale", Range(0, 4)) = 1

		[Header(Terrain Z Fighting Offset)]
		_OffsetFactor("Offset Factor", Range(0.0, -10.0)) = -1
		_OffsetUnit("Offset Unit", Range(0.0, -10.0)) = -1
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent"
			"Queue" = "Transparent-100"
			"IgnoreProjector" = "True"
		}
		LOD 200

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Back
			Offset [_OffsetFactor], [_OffsetUnit]

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag

			// Treat as a transparent surface so main light shadows use the regular shadow map
			#define _SURFACE_TYPE_TRANSPARENT 1

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _FORWARD_PLUS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fog

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
			TEXTURE2D(_Metallic); SAMPLER(sampler_Metallic);
			TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);

			// All material properties go in the CBUFFER so the SRP Batcher works
			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				float4 _Metallic_ST;
				float4 _BumpMap_ST;
				half4 _Color;
				half _MainMetallicPower1;
				half _MainSmoothnessPower1;
				half _OcclusionStrength1;
				half _BumpScale;
				half _OffsetFactor;
				half _OffsetUnit;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float2 uv         : TEXCOORD0;
				half4  color      : COLOR;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				half3  normalWS   : TEXCOORD2;
				half4  tangentWS  : TEXCOORD3; // w = tangent sign
				half4  color      : COLOR;
				float  fogFactor  : TEXCOORD4;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
				VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

				OUT.positionCS = pos.positionCS;
				OUT.positionWS = pos.positionWS;
				OUT.uv = IN.uv;
				OUT.normalWS = nrm.normalWS;
				OUT.tangentWS = half4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
				OUT.color = IN.color;
				OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				// Albedo + alpha (alpha is also multiplied by vertex alpha = the road fade in / out)
				float2 uvMain = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
				half4 albedoTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain);

				// R = metallic, G = AO, A = smoothness
				float2 uvMask = IN.uv * _Metallic_ST.xy + _Metallic_ST.zw;
				half4 mask = SAMPLE_TEXTURE2D(_Metallic, sampler_Metallic, uvMask);

				// Normal map uses the raw mesh UVs, like the original shader
				half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);

				half3 normalWS0 = normalize(IN.normalWS);
				half3 bitangentWS = cross(normalWS0, IN.tangentWS.xyz) * IN.tangentWS.w;
				half3 normalWS = TransformTangentToWorld(normalTS, half3x3(IN.tangentWS.xyz, bitangentWS, normalWS0));
				normalWS = NormalizeNormalPerPixel(normalWS);

				InputData inputData = (InputData)0;
				inputData.positionWS = IN.positionWS;
				inputData.normalWS = normalWS;
				inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
				inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
				inputData.fogCoord = IN.fogFactor;
				inputData.bakedGI = SampleSH(normalWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
				inputData.shadowMask = half4(1, 1, 1, 1);

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = albedoTex.rgb * _Color.rgb;
				surfaceData.metallic = saturate(mask.r * _MainMetallicPower1);
				surfaceData.smoothness = saturate(mask.a * _MainSmoothnessPower1);
				surfaceData.occlusion = saturate(mask.g * _OcclusionStrength1);
				surfaceData.normalTS = normalTS;
				surfaceData.alpha = albedoTex.a * _Color.a * IN.color.a;

				half4 color = UniversalFragmentPBR(inputData, surfaceData);
				color.rgb = MixFog(color.rgb, inputData.fogCoord);
				color.a = surfaceData.alpha;
				return color;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
