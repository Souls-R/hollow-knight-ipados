using System;

// Pixel-only policy, independent of Unity and GPU readback. Blank legacy HUD
// cells must never erase health, soul or Godhome binding indicators.
public static class NativeHudFrameRepair
{
    public struct Frame
    {
        public string name;
        public int x,y,width,height;
        public bool rotated;
        public Frame(string n,int a,int b,int w,int h,bool r=false) { name=n; x=a; y=b; width=w; height=h; rotated=r; }
    }
    static bool NormalHealth(string name)
    {
        return name.StartsWith("idle_v02",StringComparison.Ordinal) || name.StartsWith("refill",StringComparison.Ordinal);
    }
    static bool Critical(string name)
    {
        return NormalHealth(name) || name.StartsWith("idle",StringComparison.Ordinal) ||
            name.StartsWith("HUD_yellow_health",StringComparison.Ordinal) ||
            name.StartsWith("blue_hive_health",StringComparison.Ordinal) ||
            name.StartsWith("GG_",StringComparison.Ordinal) ||
            name.StartsWith("soul_orb_",StringComparison.Ordinal) ||
            name.StartsWith("soulorb_",StringComparison.Ordinal) ||
            name.StartsWith("level_01",StringComparison.Ordinal) ||
            name.StartsWith("health_",StringComparison.Ordinal);
    }
    static void Validate(Frame frame,int width,int height)
    {
        if (frame.name==null || frame.x<0 || frame.y<0 || frame.width<=0 || frame.height<=0 ||
            frame.width>width-frame.x || frame.height>height-frame.y)
            throw new ArgumentException("HUD frame outside its atlas");
    }
    static int Count<T>(T[] pixels,int width,Frame frame,Func<T,bool> visible)
    {
        int count=0;
        for (int y=frame.y;y<frame.y+frame.height;y++)
            for (int x=frame.x;x<frame.x+frame.width;x++) if (visible(pixels[y*width+x])) count++;
        return count;
    }
    static void LegacyIdle<T>(T[] skin,int width,Frame target,T[] source,int sourceWidth,int sourceHeight)
    {
        int uprightWidth=target.rotated?target.height:target.width;
        int uprightHeight=target.rotated?target.width:target.height;
        float scale=Math.Min((float)uprightWidth/sourceHeight,(float)uprightHeight/sourceWidth);
        float drawW=sourceHeight*scale,drawH=sourceWidth*scale;
        float padX=(uprightWidth-drawW)/2,padY=(uprightHeight-drawH)/2;
        for(int y=0;y<target.height;y++) for(int x=0;x<target.width;x++) {
            float u=(target.rotated?y:x)+.5f,v=(target.rotated?x:y)+.5f;
            T pixel=default(T);
            if(u>=padX && u<padX+drawW && v>=padY && v<padY+drawH) {
                // Legacy HUD packs store the healthy mask sideways. Undo that
                // rotation, fit its aspect, then respect the target's atlas UV.
                int sx=Math.Min(sourceWidth-1,(int)((v-padY)/drawH*sourceWidth));
                int sy=Math.Min(sourceHeight-1,(int)((1-(u-padX)/drawW)*sourceHeight));
                pixel=source[sy*sourceWidth+sx];
            }
            skin[(target.y+y)*width+target.x+x]=pixel;
        }
    }
    public static int Repair<T>(T[] skin,T[] original,int width,int height,Frame[] frames,Func<T,bool> visible,Frame? legacyMask=null)
    {
        if (width<=0 || height<=0 || (long)width*height>int.MaxValue ||
            skin.Length!=width*height || original.Length!=skin.Length || ReferenceEquals(skin,original))
            throw new ArgumentException("HUD pixel buffers");
        // Check all rectangles before changing any pixels.
        foreach (var frame in frames) Validate(frame,width,height);
        if(legacyMask.HasValue) Validate(legacyMask.Value,width,height);
        int idleCells=0, missingIdle=0;
        foreach (var frame in frames) {
            if (!frame.name.StartsWith("idle_v02",StringComparison.Ordinal)) continue;
            idleCells++;
            int native=Count(original,width,frame,visible);
            if (native>0 && (long)Count(skin,width,frame,visible)*10<native) missingIdle++;
        }
        // Old sheets have no v02 health animation. Their nonblank refill cells
        // also mean something different; rebuild idle from authored art when present,
        // and use native transition frames where the old layout is incompatible.
        bool legacyHealth=idleCells>=2 && missingIdle==idleCells;
        T[] legacyPixels=null;
        if(legacyHealth && legacyMask.HasValue) {
            var source=legacyMask.Value;
            if(Count(skin,width,source,visible)>source.width*source.height/5) {
                legacyPixels=new T[source.width*source.height];
                for(int y=0;y<source.height;y++) Array.Copy(skin,(source.y+y)*width+source.x,legacyPixels,y*source.width,source.width);
            }
        }
        int repaired=0;
        foreach (var frame in frames) {
            if (!Critical(frame.name)) continue;
            int native=Count(original,width,frame,visible);
            if (native==0) continue;
            if (!(legacyHealth && NormalHealth(frame.name)) &&
                (long)Count(skin,width,frame,visible)*10>=native) continue;
            if(legacyPixels!=null && frame.name.StartsWith("idle_v02",StringComparison.Ordinal)) {
                var source=legacyMask.Value;
                LegacyIdle(skin,width,frame,legacyPixels,source.width,source.height);
            } else for (int y=frame.y;y<frame.y+frame.height;y++)
                Array.Copy(original,y*width+frame.x,skin,y*width+frame.x,frame.width);
            repaired++;
        }
        return repaired;
    }
}
