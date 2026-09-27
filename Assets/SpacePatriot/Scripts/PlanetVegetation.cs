using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacePatriot
{
    public struct VegetationSample
    {
        public Vector3 position, up, normal;
        public float moisture, temperature, exposure, forestCover, grassHeight, grassWidth, grassDensity;
        public int plantVariant;
        public bool valid;
    }

    /// <summary>A planet-fixed cell; camera movement never changes its seed or position.</summary>
    public readonly struct WorldVegetationCell : IEquatable<WorldVegetationCell>
    {
        public readonly string WorldId;
        public readonly int Face, U, V, Divisions;
        public WorldVegetationCell(string world, int face, int u, int v, int divisions)
        { WorldId=world; Face=face; U=u; V=v; Divisions=divisions; }
        public Vector3 Direction(float fractionU=.5f, float fractionV=.5f)
        {
            PlanetVegetationCells.Basis(Face,out var n,out var right,out var forward);
            float a=((U+fractionU)/Divisions-.5f)*Mathf.PI*.5f;
            float b=((V+fractionV)/Divisions-.5f)*Mathf.PI*.5f;
            return (n+right*Mathf.Tan(a)+forward*Mathf.Tan(b)).normalized;
        }
        public int Seed(int worldSeed)
        { unchecked { return worldSeed ^ Face*83492791 ^ U*73856093 ^ V*19349663 ^ Divisions*38171; } }
        public bool Equals(WorldVegetationCell other)=>WorldId==other.WorldId&&Face==other.Face&&U==other.U&&V==other.V&&Divisions==other.Divisions;
        public override bool Equals(object other)=>other is WorldVegetationCell cell&&Equals(cell);
        public override int GetHashCode()=>Seed(WorldId==null?0:WorldId.GetHashCode());
        public override string ToString()=>WorldId+"/"+Face+"/"+Divisions+"/"+U+"/"+V;
    }

    public static class PlanetVegetationCells
    {
        public static void Basis(int face,out Vector3 normal,out Vector3 right,out Vector3 forward)
        {
            normal=face switch {0=>Vector3.right,1=>Vector3.left,2=>Vector3.up,3=>Vector3.down,4=>Vector3.forward,_=>Vector3.back};
            right=face switch {0=>Vector3.forward,1=>Vector3.back,4=>Vector3.left,_=>Vector3.right};
            forward=face switch {2=>Vector3.forward,3=>Vector3.back,_=>Vector3.up};
        }

        public static Quaternion Rotation(Vector3 up)
        {
            // Transport the northern frame to this radial up. Projecting a
            // reference forward and switching axes near its pole reversed wind
            // headings at the switch threshold. This has only the unavoidable
            // south-pole frame singularity, with no arbitrary fallback ring.
            return Quaternion.FromToRotation(Vector3.up,up);
        }

        /// <summary>Enumerate all intersecting cube faces, so edges have no aliases or missing neighbors.</summary>
        public static List<WorldVegetationCell> Near(string worldId,Vector3 direction,float spacing,float radius,int maxCells=int.MaxValue)
        {
            direction.Normalize();
            int divisions=Mathf.Max(2,Mathf.CeilToInt(Mathf.PI*.5f*FrontierWorld.PlanetRadius/spacing));
            float angle=Mathf.Min(1.2f,(radius+spacing*1.5f)/FrontierWorld.PlanetRadius);
            Quaternion frame=Rotation(direction);Vector3 right=frame*Vector3.right,forward=frame*Vector3.forward;
            var ring=new Vector3[33];ring[0]=direction;
            for(int i=0;i<32;i++) {float a=i*Mathf.PI/16;ring[i+1]=direction*Mathf.Cos(angle)+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*Mathf.Sin(angle);}
            float faceThreshold=.57735027f*Mathf.Cos(angle)-.81649658f*Mathf.Sin(angle);
            float minimumDot=Mathf.Cos(angle);
            var ranked=new List<(WorldVegetationCell cell,float distance)>();
            for(int face=0;face<6;face++)
            {
                Basis(face,out var n,out var u,out var v);
                if(Vector3.Dot(direction,n)<faceThreshold)continue;
                Vector2 low=Vector2.one*float.PositiveInfinity,high=Vector2.one*float.NegativeInfinity;
                foreach(var p in ring)
                {
                    float depth=Vector3.Dot(p,n);if(depth<=0)continue;
                    var a=new Vector2(Mathf.Atan2(Vector3.Dot(p,u),depth),Mathf.Atan2(Vector3.Dot(p,v),depth));
                    low=Vector2.Min(low,a);high=Vector2.Max(high,a);
                }
                const float limit=Mathf.PI*.25f;
                if(low.x>limit||high.x<-limit||low.y>limit||high.y<-limit)continue;
                int U(float value)=>Mathf.Clamp(Mathf.FloorToInt((value/(Mathf.PI*.5f)+.5f)*divisions),0,divisions-1);
                int minU=U(low.x),maxU=U(high.x),minV=U(low.y),maxV=U(high.y);
                for(int j=minV;j<=maxV;j++)for(int i=minU;i<=maxU;i++)
                {
                    var cell=new WorldVegetationCell(worldId,face,i,j,divisions);
                    float distance=Vector3.Dot(cell.Direction(),direction);
                    if(distance>=minimumDot)ranked.Add((cell,distance));
                }
            }
            ranked.Sort((a,b)=>b.distance.CompareTo(a.distance));
            var cells=new List<WorldVegetationCell>(Mathf.Min(maxCells,ranked.Count));
            for(int i=0;i<ranked.Count&&i<maxCells;i++)cells.Add(ranked[i].cell);
            return cells;
        }
    }

    public partial class FrontierWorld
    {
        public int VegetationRevision {get;private set;}

        Vector3 VegetationSurfacePoint(Vector3 radial)
        {
            // Height(x,z) is authored only around the northern port. Solve its
            // intersection with this radial ray; elsewhere use the globe sampler.
            Vector3 point=PlanetCenter+radial*(PlanetRadius+PlanetwideRelief(radial));
            if(radial.y>0&&new Vector2(radial.x,radial.z).magnitude*PlanetRadius<RegionRadius)
                for(int iteration=0;iteration<3;iteration++)
                {
                    float radius=(Height(point.x,point.z)-PlanetCenter.y)/radial.y;
                    point=PlanetCenter+radial*radius;
                }
            return point;
        }

        public VegetationSample SampleVegetation(Vector3 planetLocalDirection)
        {
            var result=new VegetationSample();
            if(info==null||info.biome!="temperate"||sourceSurface==null||regionClimate==null||!float.IsFinite(planetLocalDirection.sqrMagnitude)||planetLocalDirection.sqrMagnitude<.1f)return result;
            Vector3 up=planetLocalDirection.normalized;
            result.position=VegetationSurfacePoint(up);result.up=up;
            Quaternion frame=PlanetVegetationCells.Rotation(up);
            Vector3 east=frame*Vector3.right,north=frame*Vector3.forward;
            const float step=2f/PlanetRadius;
            Vector3 dx=VegetationSurfacePoint((up+east*step).normalized)-VegetationSurfacePoint((up-east*step).normalized);
            Vector3 dz=VegetationSurfacePoint((up+north*step).normalized)-VegetationSurfacePoint((up-north*step).normalized);
            result.normal=Vector3.Cross(dz,dx).normalized;
            if(Vector3.Dot(result.normal,up)<0)result.normal=-result.normal;
            Vector4 climate=regionClimate.Sample(up);
            result.moisture=climate.x;
            // Atlas export samples elevation zero. Apply the source region's
            // elevation terms here, in the original body's kilometre units.
            float elevation=Mathf.Max(0,(result.position-PlanetCenter).magnitude-PlanetRadius)*sourceSurface.SourceRadiusKm/PlanetRadius;
            result.temperature=Mathf.Clamp01(climate.y-elevation*.3f);
            result.exposure=Mathf.Clamp01(climate.z+elevation*.25f);
            Vector3 source=PlanetEngineSurface.ToSourceNormal(up);
            float x=Vector3.Dot(source,PlanetEngineSurface.SourceRight)*sourceSurface.SourceRadiusKm*1000;
            float z=Vector3.Dot(source,PlanetEngineSurface.SourceForward)*sourceSurface.SourceRadiusKm*1000;
            float patch=.5f+.5f*Mathf.Sin(x*.005f+Mathf.Sin(z*.007f)*1.8f+info.seed%17)*Mathf.Cos(z*.006f);
            result.forestCover=Mathf.Clamp((result.moisture-.22f)*1.9f+patch*.6f-.2f-result.exposure*.22f,0,.95f);
            result.grassHeight=.10f+result.moisture*.95f*(1-result.exposure*.6f);
            result.grassWidth=.022f+result.moisture*.035f;
            result.grassDensity=Mathf.Round(9000+result.moisture*24000);
            result.plantVariant=Mathf.Clamp(Mathf.FloorToInt(patch*2.999f),0,2);
            bool excluded=false;
            // These are authored northern sites. A southern point with the
            // same x/z must never inherit the port's exclusion rectangles.
            if(up.y>.98f)
            {
                var p=result.position;
                excluded=(Mathf.Abs(p.x-140)<320&&Mathf.Abs(p.z)<275)||
                    (new Vector2(p.x-outpost.x,p.z-outpost.z).magnitude<85)||
                    (new Vector2(p.x-grove.x,p.z-grove.z).magnitude<34);
            }
            result.valid=!excluded&&CanRootVegetation(result.position)&&result.moisture>=.04f&&Vector3.Dot(result.normal,up)>.55f;
            return result;
        }

        public bool TryVegetationFocus(Vector3 worldEye,out VegetationSample sample,out float altitude)
        {
            Vector3 local=transform.InverseTransformPoint(worldEye);
            sample=SampleVegetation(local-PlanetCenter);
            altitude=float.PositiveInfinity;
            if(sample.up.sqrMagnitude<.5f)return false;
            altitude=Vector3.Dot(worldEye-transform.TransformPoint(sample.position),transform.TransformDirection(sample.up).normalized);
            return true;
        }
    }
}
