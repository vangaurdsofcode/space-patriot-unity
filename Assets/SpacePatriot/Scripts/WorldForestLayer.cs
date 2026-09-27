using System.Collections;
using UnityEngine;
namespace SpacePatriot
{
    /// <summary>Source ForestStage: a deterministic planet-fixed 240 m neighborhood.</summary>
    public sealed class WorldForestLayer : MonoBehaviour
    {
        const float CellSize=14f,Radius=240f;
        public const int MaximumTrees=2000;
        public int TreeCount {get;private set;}
        public bool IsBuilding=>buildJob!=null;
        FrontierWorld world;Transform current,pending;Coroutine buildJob;
        Vector3 builtAt=Vector3.positiveInfinity,buildingAt;int revision;
        GameObject[] trees;
        public void Initialize(FrontierWorld owner)
        {
            world=owner;revision=world.VegetationRevision;
            trees=world.info.id=="earth"?new[]{Resources.Load<GameObject>("OriginalShips/conifer")}:new[]{Resources.Load<GameObject>("OriginalShips/alien-flora-0"),Resources.Load<GameObject>("OriginalShips/alien-flora-1"),Resources.Load<GameObject>("OriginalShips/alien-flora-2")};
        }
        void Update(){var camera=FrontierGame.Instance?.view;if(camera!=null)UpdateAround(camera.transform.position);}
        public void UpdateAround(Vector3 worldEye)
        {
            if(world==null||trees==null)return;
            if(revision!=world.VegetationRevision){Cancel();if(current!=null)Destroy(current.gameObject);current=null;TreeCount=0;builtAt=Vector3.positiveInfinity;revision=world.VegetationRevision;}
            bool active=world.TryVegetationFocus(worldEye,out var focus,out float altitude)&&altitude<500&&altitude> -8;
            if(current!=null)current.gameObject.SetActive(active);
            if(!active){Cancel();return;}
            if(buildJob!=null&&(focus.position-buildingAt).sqrMagnitude>Radius*Radius*4)Cancel();
            if(buildJob==null&&(focus.position-builtAt).sqrMagnitude>96*96){buildingAt=focus.position;buildJob=StartCoroutine(BuildNeighborhood(focus,revision));}
        }
        IEnumerator BuildNeighborhood(VegetationSample focus,int generation)
        {
            // Yield once before work so synchronous completion cannot leave a stale Coroutine handle.
            yield return null;
            pending=new GameObject("ForestStage / streamed trees").transform;pending.SetParent(transform,false);pending.gameObject.SetActive(false);
            var cells=PlanetVegetationCells.Near(world.info.id,focus.up,CellSize,Radius,2400);int count=0,attempts=0;float started=Time.realtimeSinceStartup;
            foreach(var cell in cells)
            {
                // Count rejected candidates too; dry worlds must not monopolize an update.
                if(++attempts%32==0&&Time.realtimeSinceStartup-started>.004f){yield return null;started=Time.realtimeSinceStartup;}
                var random=new System.Random(cell.Seed(world.info.seed)^38171);float R()=>(float)random.NextDouble();
                var point=world.SampleVegetation(cell.Direction(.1f+R()*.8f,.1f+R()*.8f));
                if(!point.valid||point.moisture<.08f||(point.position-focus.position).sqrMagnitude>Radius*Radius||R()>Mathf.Min(.95f,Mathf.Max(.12f,point.forestCover)*1.2f))continue;
                int variant=trees.Length==1?0:Mathf.Clamp(point.plantVariant,0,trees.Length-1);var prefab=trees[variant];if(prefab==null)continue;
                var tree=Instantiate(prefab,pending);tree.name="Regional forest / "+cell;
                tree.transform.localPosition=point.position;tree.transform.localRotation=PlanetVegetationCells.Rotation(point.up)*Quaternion.AngleAxis(R()*360,Vector3.up);
                tree.transform.localScale=Vector3.one*(.55f+R()*.85f)*(1.2f-point.exposure*.35f);
                foreach(var collider in tree.GetComponentsInChildren<Collider>())collider.enabled=false;
                foreach(var renderer in tree.GetComponentsInChildren<Renderer>())renderer.gameObject.isStatic=true;
                count++;if(count>=MaximumTrees)break;
            }
            if(generation!=world.VegetationRevision){Cancel();yield break;}
            var old=current;
            StaticBatchingUtility.Combine(pending.gameObject);current=pending;pending=null;current.gameObject.SetActive(true);builtAt=focus.position;TreeCount=count;buildJob=null;
            if(old!=null)Destroy(old.gameObject);
        }
        void Cancel(){if(buildJob!=null){StopCoroutine(buildJob);buildJob=null;}if(pending!=null)Destroy(pending.gameObject);pending=null;}
        void OnDestroy(){Cancel();if(current!=null)Destroy(current.gameObject);}
    }
}


