using UnityEngine;

namespace SpacePatriot
{
    public partial class FrontierWorld
    {
        static readonly Color SpaceColor=new Color(.0007f,.0014f,.0035f,1);
        Material radialSky;
        Material previousSky;

        public float RadialAltitude(Vector3 observer)
        {
            float scale=transform.lossyScale.x;
            return Mathf.Max(0,(observer-transform.TransformPoint(PlanetCenter)).magnitude-PlanetRadius*scale);
        }

        static Color SourceAtmosphereTint(WorldInfo world)
        {
            Color tint=world.biome switch
            {
                "desert"=>new Color(.62f,.34f,.16f),
                "volcanic"=>new Color(.65f,.22f,.095f),
                "ice"=>new Color(.36f,.59f,.75f),
                "gas"=>new Color(.32f,.46f,.58f),
                _=>new Color(.22f,.49f,.76f)
            };
            if(world.AlienPhotosynthesis&&(world.biome=="temperate"||world.biome=="ice"))
            {
                Color[] redStarTints={new Color(.44f,.25f,.37f),new Color(.39f,.31f,.48f),new Color(.34f,.42f,.39f)};
                tint=redStarTints[(int)((uint)world.seed%3)];
            }
            return tint;
        }

        static float AirStrength(WorldInfo world)
        {
            if(world.id=="mercury")return 0;
            if(world.id=="mars")return .18f;
            if(world.id=="venus")return 1.45f;
            return world.biome switch
            {
                "gas"=>1.25f,
                "temperate"=>1f,
                "ice"=>.65f,
                "volcanic"=>.55f,
                "desert"=>.7f,
                _=>.18f
            };
        }

        void EnsureRadialSky()
        {
            if(radialSky!=null)return;
            var shader=Resources.Load<Shader>("Shaders/RadialSky");
            if(shader==null||!shader.isSupported)
            {
                Debug.LogError("RadialSky shader is missing or unsupported; the planetary atmosphere cannot render.");
                return;
            }
            previousSky=RenderSettings.skybox;
            radialSky=new Material(shader){name="Worldworks / radial atmosphere",hideFlags=HideFlags.DontSave};
        }

        void UpdateRadialAtmosphere(Camera camera,Vector3 observer)
        {
            if(camera==null||info==null)return;
            EnsureRadialSky();
            Vector3 center=transform.TransformPoint(PlanetCenter);
            Vector3 up=(observer-center).normalized;
            if(up.sqrMagnitude<.5f)up=transform.up;
            float altitude=RadialAltitude(observer);
            float air=AirStrength(info);
            float atmosphereHeight=650*Mathf.Max(.5f,air);
            Vector3 sunDirection=RenderSettings.sun!=null?-RenderSettings.sun.transform.forward:Vector3.up;
            float starWarmth=Mathf.Clamp01((5772-info.stellarTemperatureK)/4000);
            Color starTint=new Color(1,1-starWarmth*.26f,1-starWarmth*.45f);
            if(info.stellarTemperatureK>6500)starTint.r=Mathf.Max(.75f,1-(info.stellarTemperatureK-6500)/20000);
            Color airTint=SourceAtmosphereTint(info);
            if(radialSky!=null)
            {
                radialSky.SetVector("_PlanetUp",up);
                radialSky.SetVector("_SunDirection",sunDirection);
                radialSky.SetFloat("_PlanetRadius",PlanetRadius*transform.lossyScale.x);
                radialSky.SetFloat("_AltitudeMeters",altitude);
                radialSky.SetFloat("_AtmosphereHeight",atmosphereHeight);
                radialSky.SetFloat("_AirStrength",air);
                radialSky.SetColor("_AirTint",airTint);
                radialSky.SetColor("_StarTint",starTint);
                if(RenderSettings.skybox!=radialSky)RenderSettings.skybox=radialSky;
            }
            float fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(atmosphereHeight*.7f,atmosphereHeight*1.8f,altitude));
            float fog=air>.001f?.00022f*air*Mathf.Exp(-altitude/(atmosphereHeight*.21f))*fade:0;
            RenderSettings.fogDensity=fog;
            float sunElevation=Vector3.Dot(up,sunDirection);
            float day=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.14f,.26f,sunElevation));
            RenderSettings.fogColor=new Color((.07f+airTint.r*.65f)*starTint.r,(.07f+airTint.g*.65f)*starTint.g,(.07f+airTint.b*.65f)*starTint.b)*(.02f+.98f*day);
            float space=Mathf.SmoothStep(0,1,Mathf.InverseLerp(atmosphereHeight*.3f,atmosphereHeight*1.5f,altitude));
            if(orbit!=null)orbit.gameObject.SetActive(space>.05f);
            camera.clearFlags=CameraClearFlags.Skybox;
            camera.backgroundColor=SpaceColor;
            camera.farClipPlane=80000;
        }

        void OnDestroy()
        {
            if(radialSky!=null)
            {
                if(RenderSettings.skybox==radialSky)RenderSettings.skybox=previousSky;
                Destroy(radialSky);
            }
        }
    }
}
