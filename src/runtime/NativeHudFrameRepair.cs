using System;

// Pixel-only policy, independent of Unity and GPU readback. Blank legacy HUD
// cells must never erase health, soul or Godhome binding indicators.
public static class NativeHudFrameRepair
{
    public struct Frame
    {
        public string name;
        public int x,y,width,height;
        public Frame(string n,int a,int b,int w,int h) { name=n; x=a; y=b; width=w; height=h; }
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
    public static int Repair<T>(T[] skin,T[] original,int width,int height,Frame[] frames,Func<T,bool> visible)
    {
        if (width<=0 || height<=0 || (long)width*height>int.MaxValue ||
            skin.Length!=width*height || original.Length!=skin.Length || ReferenceEquals(skin,original))
            throw new ArgumentException("HUD pixel buffers");
        // Check all rectangles before changing any pixels.
        foreach (var frame in frames) Validate(frame,width,height);
        int idleCells=0, missingIdle=0;
        foreach (var frame in frames) {
            if (!frame.name.StartsWith("idle_v02",StringComparison.Ordinal)) continue;
            idleCells++;
            int native=Count(original,width,frame,visible);
            if (native>0 && (long)Count(skin,width,frame,visible)*10<native) missingIdle++;
        }
        // Old sheets have no v02 health animation. Their nonblank refill cells
        // also mean something different, so restore that complete animation.
        bool legacyHealth=idleCells>=2 && missingIdle==idleCells;
        int repaired=0;
        foreach (var frame in frames) {
            if (!Critical(frame.name)) continue;
            int native=Count(original,width,frame,visible);
            if (native==0) continue;
            if (!(legacyHealth && NormalHealth(frame.name)) &&
                (long)Count(skin,width,frame,visible)*10>=native) continue;
            for (int y=frame.y;y<frame.y+frame.height;y++)
                Array.Copy(original,y*width+frame.x,skin,y*width+frame.x,frame.width);
            repaired++;
        }
        return repaired;
    }
}
