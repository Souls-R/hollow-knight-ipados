using System;
using System.Collections.Generic;
using UnityEngine;

public static class NativeHudCompatibility
{
    // ASTC game textures are not CPU-readable. Decode only the native HUD once
    // per skin selection, on the GPU, then discard both CPU pixel buffers.
    public static int Prepare(Texture2D skin,tk2dSpriteCollectionData collection)
    {
        if (!skin.isReadable) return 0;
        if (collection.textures==null || collection.textures.Length!=1 || !collection.textures[0])
            throw new InvalidOperationException("Native HUD atlas unavailable");
        var frames=new List<NativeHudFrameRepair.Frame>();
        foreach (var def in collection.spriteDefinitions) {
            if (def==null || def.uvs==null || def.uvs.Length<4) continue;
            float minX=1,minY=1,maxX=0,maxY=0;
            foreach (var uv in def.uvs) { minX=Math.Min(minX,uv.x); minY=Math.Min(minY,uv.y); maxX=Math.Max(maxX,uv.x); maxY=Math.Max(maxY,uv.y); }
            int x=Mathf.RoundToInt(minX*skin.width), y=Mathf.RoundToInt(minY*skin.height);
            int w=Mathf.RoundToInt(maxX*skin.width)-x, h=Mathf.RoundToInt(maxY*skin.height)-y;
            if (w>0 && h>0) frames.Add(new NativeHudFrameRepair.Frame(def.name,x,y,w,h));
        }
        RenderTexture previous=RenderTexture.active, target=null;
        Texture2D readable=null;
        try {
            target=RenderTexture.GetTemporary(skin.width,skin.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Graphics.Blit(collection.textures[0],target);
            RenderTexture.active=target;
            readable=new Texture2D(skin.width,skin.height,TextureFormat.RGBA32,false);
            readable.ReadPixels(new Rect(0,0,skin.width,skin.height),0,0,false);
            var pixels=skin.GetPixels32();
            int count=NativeHudFrameRepair.Repair(pixels,readable.GetPixels32(),skin.width,skin.height,frames.ToArray(),p=>p.a>=16);
            if (count>0) skin.SetPixels32(pixels);
            skin.Apply(false,true);
            return count;
        } finally {
            RenderTexture.active=previous;
            if (target) RenderTexture.ReleaseTemporary(target);
            if (readable) UnityEngine.Object.Destroy(readable);
        }
    }
}
