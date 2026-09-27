using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpacePatriot;

public static class ShipAssetBaker
{
    static string Key(string name)
    {
        using(var sha=SHA256.Create())return System.BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(name))).Replace("-","").Substring(0,16);
    }
    public static void Bake()
    {
        const string root="Assets/SpacePatriot/Models";
        Directory.CreateDirectory(root+"/Geometry");Directory.CreateDirectory(root+"/Materials");Directory.CreateDirectory(root+"/Textures");Directory.CreateDirectory("Assets/SpacePatriot/Resources/Ships");AssetDatabase.Refresh();
        for(int i=0;i<ShipSpec.Fleet.Length;i++)
        {
            if(ShipSpec.Fleet[i].family<0)continue; // Authored prototypes are registered separately.
            var ship=IndustrialArt.Ship(null,i,out _,out _);
            foreach(var filter in ship.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;if(mesh==null||!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))continue;
                string path=root+"/Geometry/"+Key(mesh.name)+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing==null){AssetDatabase.CreateAsset(mesh,path);}else{EditorUtility.CopySerialized(mesh,existing);filter.sharedMesh=existing;EditorUtility.SetDirty(existing);}
            }
            foreach(var renderer in ship.GetComponentsInChildren<Renderer>(true))
            {
                var mat=renderer.sharedMaterial;if(mat==null||!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))continue;
                foreach(var property in mat.GetTexturePropertyNames())
                {
                    var texture=mat.GetTexture(property) as Texture2D;if(texture==null||!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))continue;
                    string path=root+"/Textures/"+Key(texture.name)+".png";
                    if(!File.Exists(path)){File.WriteAllBytes(path,texture.EncodeToPNG());AssetDatabase.ImportAsset(path);}
                    mat.SetTexture(property,AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                }
                string materialPath=root+"/Materials/"+Key(mat.name)+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(existing==null)AssetDatabase.CreateAsset(mat,materialPath);else{EditorUtility.CopySerialized(mat,existing);renderer.sharedMaterial=existing;EditorUtility.SetDirty(existing);}
            }
            PrefabUtility.SaveAsPrefabAsset(ship.gameObject,"Assets/SpacePatriot/Resources/Ships/"+ShipSpec.Fleet[i].name+".prefab");Object.DestroyImmediate(ship.gameObject);
        }
        AssetDatabase.SaveAssets();Debug.Log("SPACE_PATRIOT_SHIPS_BAKED");
    }
}
