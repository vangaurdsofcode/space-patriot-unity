using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
public static class VegetationDaylightValidation
{
    public static void Run()
    {
        const int size=256;var lines=new List<string>();
        var target=new RenderTexture(size,size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        var pixels=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
        var previous=RenderTexture.active;var sun=Shader.GetGlobalVector("_MainLightPosition");var eye=Shader.GetGlobalVector("_WorldSpaceCameraPos");var screen=Shader.GetGlobalVector("_ScreenParams");
        string[] fog={"FOG_LINEAR","FOG_EXP","FOG_EXP2"};bool[] enabled=Array.ConvertAll(fog,Shader.IsKeywordEnabled);
        try
        {
            foreach(var keyword in fog)Shader.DisableKeyword(keyword);
            Check("Grassworks",.055f,false);Check("GaussianVegetation",.06f,true);
            Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/vegetation-daylight.txt",lines);Debug.Log("VEGETATION_DAYLIGHT_PASS 2");
        }
        finally
        {
            RenderTexture.active=previous;Shader.SetGlobalVector("_MainLightPosition",sun);Shader.SetGlobalVector("_WorldSpaceCameraPos",eye);Shader.SetGlobalVector("_ScreenParams",screen);
            for(int i=0;i<fog.Length;i++)if(enabled[i])Shader.EnableKeyword(fog[i]);else Shader.DisableKeyword(fog[i]);
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
        }
        void Check(string name,float expected,bool instanced)
        {
            var shader=Resources.Load<Shader>("Shaders/"+name);if(shader==null||!shader.isSupported)throw new Exception(name+" shader unsupported");
            var material=new Material(shader){enableInstancing=instanced};var mesh=new Mesh{name="Daylight fixture"};
            mesh.vertices=new[]{new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(-1,2,0)};
            mesh.uv=instanced?new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)}:new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
            mesh.uv2=new[]{new Vector2(.2f,1),new Vector2(.2f,1),new Vector2(.2f,1),new Vector2(.2f,1)};
            mesh.colors=new[]{Color.green,Color.green,Color.green,Color.green};mesh.triangles=new[]{0,1,2,0,2,3};mesh.bounds=new Bounds(Vector3.zero,Vector3.one*6);
            var block=new MaterialPropertyBlock();block.SetVectorArray("_InstanceTint",new[]{Vector4.one});block.SetVector("_Range",new Vector4(0,0,400,450));block.SetFloat("_Fade",1);block.SetFloat("_Wind",0);block.SetFloat("_FogDensity",0);
            try
            {
                double day=Render(Vector3.up),night=Render(Vector3.down),ratio=night/day;
                if(day<1||Math.Abs(ratio-expected)>.003)throw new Exception(name+": incorrect day/night pixel energy "+ratio+" expected "+expected);
                lines.Add($"PASS: {name} actual GPU night/day pixel energy {ratio:F5}; source target {expected:F5}.");
            }
            finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
            double Render(Vector3 direction)
            {
                using(var command=new CommandBuffer{name="Source vegetation day/night acceptance"})
                {
                    command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.clear);command.SetViewport(new Rect(0,0,size,size));
                    command.SetViewProjectionMatrices(Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.Translate(new Vector3(0,-1,5)),GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,1,.1f,500),true));
                    command.SetGlobalVector("_MainLightPosition",new Vector4(direction.x,direction.y,direction.z,0));command.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(0,1,-5,1));command.SetGlobalVector("_ScreenParams",new Vector4(size,size,1+1f/size,1+1f/size));
                    if(instanced)command.DrawMeshInstanced(mesh,0,material,0,new[]{Matrix4x4.TRS(Vector3.up,Quaternion.identity,Vector3.one*.4f)},1,block);else command.DrawMesh(mesh,Matrix4x4.identity,material,0,0,block);
                    Graphics.ExecuteCommandBuffer(command);
                }
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size,size),0,0,false);pixels.Apply(false,false);double sum=0;foreach(var color in pixels.GetPixels())sum+=color.r+color.g+color.b;return sum;
            }
        }
    }
}
