using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static SpacePatriot.IndustrialArt;
namespace SpacePatriot
{
    public partial class FrontierWorld
    {
        public const float PlanetRadius=18000, RegionRadius=3000, RingStep=25;
        // Oceanworks' authored water datum is four metres below the terrain
        // sphere. Share this radial level with vegetation, including far from port.
        public const float SeaLevel=-4, VegetationShoreClearance=2;
        public const int LongitudeCount=384;
        const float AngleStep=2*Mathf.PI/LongitudeCount;
        public WorldworksTerrain terrainFields;
        public Grassworks grass;
        PlanetRegionClimate regionClimate;
        PlanetEngineSurface sourceSurface;
        Mesh streamedTerrainMesh;
        Transform streamedTerrainRoot;
        MeshCollider streamedTerrainCollider;
        MeshCollider planetCollider;
        Material terrainMaterial;
        Vector3 streamedTerrainAnchor;
        bool streamedTerrainWalking;
        Matrix4x4 surfaceWorldMatrix;
        public Vector3 PlanetCenter=>new Vector3(0,-PlanetRadius-3,0);
        public float ElevationAboveSea(Vector3 planetLocalPoint)
        {
            return (planetLocalPoint-PlanetCenter).magnitude-(PlanetRadius+SeaLevel);
        }
        public bool CanRootVegetation(Vector3 planetLocalSurface)
        {
            // Original VegetationStage.point rejects ground below sea + .002 km.
            // A negative world Y is not water on a curved planet.
            return ElevationAboveSea(planetLocalSurface)>=VegetationShoreClearance;
        }
        float PlanetwideRelief(Vector3 normal)
        {
            return sourceSurface.SourceHeightMeters(normal);
        }
        public float RawPlanetHeight(float x,float z)
        {
            float radius=new Vector2(x,z).magnitude;
            float curvature=Mathf.Sqrt(Mathf.Max(0,PlanetRadius*PlanetRadius-radius*radius))-PlanetRadius-3;
            if(info.biome=="gas")return curvature;
            // Update can run during scene startup before Generate has loaded the
            // source fields. Keep the collision query finite until they are ready.
            if(terrainFields==null||sourceSurface==null)return curvature;
            // Preserve the port's working plane; fade it into the original engine's hills.
            float flatDistance=new Vector2(Mathf.Max(0,Mathf.Abs(x-120)-315),Mathf.Max(0,Mathf.Abs(z)-260)).magnitude;
            float basin=Mathf.SmoothStep(0,1,flatDistance/230);
            float edge=1-Mathf.SmoothStep(0,1,(radius-2400)/600);
            float relief=terrainFields.Sample(x,z).x;
            var radial=new Vector3(x,PlanetRadius+curvature,z).normalized;
            float globe=PlanetwideRelief(radial);
            float localGeology=sourceSurface.HeightMeters(radial);
            float geology=Mathf.Lerp(globe,localGeology,edge);
            return Mathf.Lerp(-3,curvature+geology+Mathf.Clamp(relief,-16,55)*.62f*edge,basin);
        }
        Vector3 RingVertex(int ring,int sector)
        {
            float a=sector*AngleStep,r=ring*RingStep,x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
            return new Vector3(x,RawPlanetHeight(x,z),z);
        }
        static float TriangleHeight(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
            float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/den;
            float v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/den;
            return a.y*u+b.y*v+c.y*(1-u-v);
        }
        public float Height(float x,float z)
        {
            float radius=new Vector2(x,z).magnitude;if(radius<.001f)return RawPlanetHeight(0,0);
            if(radius>=RegionRadius-1)return RawPlanetHeight(x,z);
            float angle=Mathf.Repeat(Mathf.Atan2(z,x),Mathf.PI*2);int sector=Mathf.FloorToInt(angle/AngleStep);
            // Rings are polygons. Use the chord distance, not circular radius, to select a cell.
            float radial=radius*Mathf.Cos(angle-(sector+.5f)*AngleStep)/Mathf.Cos(AngleStep*.5f);
            int ring=Mathf.FloorToInt(radial/RingStep);
            Vector3 p=new Vector3(x,0,z),a=RingVertex(ring,sector),b=RingVertex(ring,sector+1),c=RingVertex(ring+1,sector),d=RingVertex(ring+1,sector+1);
            if(ring==0)return TriangleHeight(p,a,c,d);
            float side=(c.x-b.x)*(p.z-b.z)-(c.z-b.z)*(p.x-b.x);
            float aside=(c.x-b.x)*(a.z-b.z)-(c.z-b.z)*(a.x-b.x);
            return side*aside>=0?TriangleHeight(p,a,b,c):TriangleHeight(p,b,d,c);
        }
        /// <summary>Find the nearest solid point on the closed planet, including its sides and far hemisphere.</summary>
        public bool TrySurface(Vector3 near,out Vector3 point,out Vector3 normal)
        {
            point=default;normal=Vector3.up;
            if(info==null||info.biome=="gas")return false;
            // A moved/rotated planet must update PhysX before querying its
            // colliders; otherwise the first streamed patch uses the old pose.
            var currentMatrix=transform.localToWorldMatrix;
            if(surfaceWorldMatrix!=currentMatrix)
            {
                Physics.SyncTransforms();surfaceWorldMatrix=currentMatrix;
                streamedTerrainAnchor=Vector3.positiveInfinity;
            }
            if(planetCollider==null)
            {
                var shell=content!=null?content.Find("Continuous spherical terrain"):null;
                if(shell!=null)planetCollider=shell.GetComponent<MeshCollider>();
            }
            if(planetCollider==null)return false;
            Vector3 center=transform.TransformPoint(PlanetCenter);
            Vector3 radial=near-center;
            if(radial.sqrMagnitude<1)radial=Vector3.up;
            radial.Normalize();
            Vector3 origin=center+radial*(PlanetRadius+2200);
            var ray=new Ray(origin,-radial);
            if(streamedTerrainCollider&&streamedTerrainRoot&&streamedTerrainRoot.gameObject.activeInHierarchy&&streamedTerrainCollider.Raycast(ray,out var streamedHit,3600))
            {point=streamedHit.point;normal=streamedHit.normal.normalized;if(Vector3.Dot(normal,radial)<0)normal=-normal;return true;}
            if(!planetCollider.Raycast(ray,out var hit,3600))return false;
            point=hit.point;normal=hit.normal.normalized;
            if(Vector3.Dot(normal,radial)<0)normal=-normal;
            return true;
        }
        void Terrain()
        {
            terrainFields=new WorldworksTerrain(info.id);
            regionClimate=new PlanetRegionClimate(info.id);
            sourceSurface=new PlanetEngineSurface(info);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var fields=new List<Color>();var indices=new List<int>();
            void Add(Vector3 p,float local){var field=terrainFields.Sample(p.x,p.z);vertices.Add(p);uv.Add(new Vector2(local,0));fields.Add(new Color(field.y,field.z,field.w,1));}
            Add(new Vector3(0,RawPlanetHeight(0,0),0),1);
            const int localRings=120,globalRings=240;int rings=localRings+globalRings;float cap=Mathf.Asin(RegionRadius/PlanetRadius);
            for(int ring=1;ring<=rings;ring++)for(int j=0;j<LongitudeCount;j++)
            {
                Vector3 p;float local=0;
                if(ring<=localRings){p=RingVertex(ring,j);local=1-Mathf.SmoothStep(0,1,(ring*RingStep-2100)/900);}
                else{float theta=Mathf.Lerp(cap,Mathf.PI,(ring-localRings)/(globalRings+1f)),a=j*AngleStep;var radial=new Vector3(Mathf.Sin(theta)*Mathf.Cos(a),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(a));p=PlanetCenter+radial*(PlanetRadius+PlanetwideRelief(radial));}
                Add(p,local);
            }
            Add(PlanetCenter-Vector3.up*PlanetRadius,0);
            void Tri(int a,int b,int c){var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(Vector3.Dot(n,vertices[a]-PlanetCenter)<0){int t=b;b=c;c=t;}indices.Add(a);indices.Add(b);indices.Add(c);}
            for(int j=0;j<LongitudeCount;j++)Tri(0,1+j,1+(j+1)%LongitudeCount);
            for(int ring=0;ring<rings-1;ring++)for(int j=0;j<LongitudeCount;j++){int a=1+ring*LongitudeCount+j,b=1+ring*LongitudeCount+(j+1)%LongitudeCount,c=a+LongitudeCount,d=b+LongitudeCount;Tri(a,b,c);Tri(b,d,c);}
            for(int j=0;j<LongitudeCount;j++)Tri(vertices.Count-1,1+(rings-1)*LongitudeCount+j,1+(rings-1)*LongitudeCount+(j+1)%LongitudeCount);
            var mesh=new Mesh{name=info.name+" / continuous Worldworks planet",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(fields);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();generatedAssets.Add(mesh);
            var source=Resources.Load<Material>("OriginalSurfaces/geology-"+(info.biome=="desert"?5:info.biome=="ice"?10:info.biome=="volcanic"?14:info.biome=="temperate"?6:8));
            terrainMaterial=new Material(Resources.Load<Shader>("Shaders/WorldworksPlanet"));
            terrainMaterial.SetTexture("_GroundMap",source.GetTexture("_BaseMap"));terrainMaterial.SetTexture("_RockMap",Resources.Load<Material>("OriginalSurfaces/geology-"+(info.biome=="desert"?1:info.biome=="ice"?9:2)).GetTexture("_BaseMap"));
            terrainMaterial.SetTexture("_NoiseVolume",PlanetEngineSurface.NoiseVolume);
            terrainMaterial.SetFloat("_Living",info.biome=="temperate"?1:0);terrainMaterial.SetFloat("_Seed",info.seed%997);
            terrainMaterial.SetVector("_PlanetCenter",transform.TransformPoint(PlanetCenter));terrainMaterial.SetFloat("_PlanetRadius",PlanetRadius*Mathf.Max(transform.lossyScale.x,Mathf.Max(transform.lossyScale.y,transform.lossyScale.z)));
            terrainMaterial.SetFloat("_WorldType",sourceSurface.MaterialType);terrainMaterial.SetFloat("_WorldAmplitude",sourceSurface.Amplitude);terrainMaterial.SetFloat("_Frequency",sourceSurface.Frequency);terrainMaterial.SetFloat("_TerrainBase",sourceSurface.TerrainBase);terrainMaterial.SetVector("_NoiseOffset",sourceSurface.NoiseOffset);
            terrainMaterial.SetVector("_SourceRight",PlanetEngineSurface.SourceRight);terrainMaterial.SetVector("_SourceUp",PlanetEngineSurface.SourceUp);terrainMaterial.SetVector("_SourceForward",PlanetEngineSurface.SourceForward);
            terrainMaterial.SetVector("_WorldTint",SourceWorldTint());terrainMaterial.SetFloat("_Liquid",SourceWorldLiquid());generatedAssets.Add(terrainMaterial);
            var ground=MeshObject("Continuous spherical terrain",content,mesh,terrainMaterial,Vector3.zero,Vector3.one);planetCollider=ground.AddComponent<MeshCollider>();planetCollider.sharedMesh=mesh;
        }
        Color SourceWorldTint()
        {
            return sourceSurface.MaterialType switch
            {
                0=>new Color(1,.96f,1.08f),1=>new Color(.79f,1.15f,.77f),2=>new Color(1.27f,.9f,.65f),3=>new Color(1.11f,.91f,1.2f),4=>new Color(.77f,.88f,1.28f),5=>new Color(1.1f,.7f,.51f),_=>Color.white
            };
        }
        float SourceWorldLiquid()
        {
            if(sourceSurface.MaterialType==5)return 2;
            if(sourceSurface.MaterialType==0||sourceSurface.MaterialType==3||info.name=="Mercury"||info.name=="Venus"||info.name=="Mars")return 0;
            return sourceSurface.MaterialType is 1 or 2 or 4?1:0;
        }
        public void StreamSurface(Vector3 focus,bool walking)
        {
            if(info==null||info.biome=="gas"||!TrySurface(focus,out var ground,out var normal)||Vector3.Dot(focus-ground,normal)>650)
            {if(streamedTerrainRoot)streamedTerrainRoot.gameObject.SetActive(false);return;}
            float recenter=walking?120:1200;
            if(streamedTerrainMesh&&streamedTerrainWalking==walking&&Vector3.Distance(focus,streamedTerrainAnchor)<recenter)
            {if(!streamedTerrainRoot.gameObject.activeSelf)streamedTerrainRoot.gameObject.SetActive(true);return;}
            BuildStreamedSurface(focus,ground,normal,walking);
        }
        void BuildStreamedSurface(Vector3 focus,Vector3 ground,Vector3 normal,bool walking)
        {
            const float halfSize=6000,offset=.035f;int segments=walking?192:96,side=segments+1,count=side*side;
            var vertices=new Vector3[count];var uvs=new Vector2[count];var fields=new Color[count];var triangles=new int[segments*segments*6];
            float Spread(float value)=>Mathf.Sign(value)*Mathf.Pow(Mathf.Abs(value),2.1f);
            Vector3 focusLocal=transform.InverseTransformPoint(ground),normalLocal=(focusLocal-PlanetCenter).normalized;
            bool portPatch=normalLocal.y>.92f&&new Vector2(focusLocal.x,focusLocal.z).magnitude<RegionRadius+100;
            Vector3 north=Vector3.ProjectOnPlane(Vector3.forward,normalLocal);
            if(north.sqrMagnitude<.01f)north=Vector3.ProjectOnPlane(Vector3.right,normalLocal);
            north.Normalize();Vector3 east=Vector3.Cross(normalLocal,north).normalized;
            Quaternion frame=portPatch?Quaternion.identity:Quaternion.LookRotation(north,normalLocal);
            Vector3 baseCenter=portPatch?focusLocal:PlanetCenter+normalLocal*(PlanetRadius+PlanetwideRelief(normalLocal));
            for(int z=0;z<side;z++)
            {
                float localZ=Spread(z/(float)segments*2-1)*halfSize;
                for(int x=0;x<side;x++)
                {
                    float localX=Spread(x/(float)segments*2-1)*halfSize;int k=z*side+x;
                    if(portPatch)
                    {
                        float worldX=focusLocal.x+localX,worldZ=focusLocal.z+localZ,y=Height(worldX,worldZ);
                        vertices[k]=new Vector3(worldX-baseCenter.x,y-baseCenter.y+offset,worldZ-baseCenter.z);
                        var climate=regionClimate.Sample(new Vector3(worldX,PlanetRadius+y+3,worldZ).normalized);
                        fields[k]=new Color(climate.x,climate.y,climate.z,climate.w);
                    }
                    else
                    {
                        Vector3 tangent=east*localX+north*localZ;
                        Vector3 radial=(normalLocal*PlanetRadius+tangent).normalized;
                        var climate=regionClimate.Sample(radial);
                        float h=PlanetwideRelief(radial);
                        Vector3 point=PlanetCenter+radial*(PlanetRadius+h+offset);
                        Vector3 delta=point-baseCenter;vertices[k]=new Vector3(Vector3.Dot(delta,east),Vector3.Dot(delta,normalLocal),Vector3.Dot(delta,north));
                        fields[k]=new Color(climate.x,climate.y,climate.z,climate.w);
                    }
                    uvs[k]=new Vector2(1,0);
                }
            }
            int t=0;for(int z=0;z<segments;z++)for(int x=0;x<segments;x++)
            {int a=z*side+x,b=a+1,c=a+side,d=c+1;triangles[t++]=a;triangles[t++]=c;triangles[t++]=b;triangles[t++]=b;triangles[t++]=c;triangles[t++]=d;}
            var next=new Mesh{name=info.name+" / streamed Worldworks terrain",indexFormat=IndexFormat.UInt32};next.vertices=vertices;next.uv=uvs;next.colors=fields;next.triangles=triangles;next.RecalculateNormals();next.RecalculateBounds();
            if(!streamedTerrainRoot){streamedTerrainRoot=new GameObject("Worldworks / moving terrain patch").transform;streamedTerrainRoot.SetParent(transform,false);streamedTerrainRoot.gameObject.AddComponent<MeshFilter>();var renderer=streamedTerrainRoot.gameObject.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;streamedTerrainCollider=streamedTerrainRoot.gameObject.AddComponent<MeshCollider>();}
            streamedTerrainRoot.GetComponent<MeshRenderer>().sharedMaterial=terrainMaterial;
            streamedTerrainRoot.localPosition=baseCenter;streamedTerrainRoot.localRotation=frame;streamedTerrainRoot.GetComponent<MeshFilter>().sharedMesh=next;streamedTerrainCollider.sharedMesh=null;streamedTerrainCollider.sharedMesh=next;
            if(streamedTerrainMesh)Destroy(streamedTerrainMesh);streamedTerrainMesh=next;streamedTerrainAnchor=focus;streamedTerrainWalking=walking;streamedTerrainRoot.gameObject.SetActive(true);Physics.SyncTransforms();
        }
    }
}
