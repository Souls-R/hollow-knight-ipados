using System;

public static class HudFrameRepairTests
{
    static void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
    static NativeHudFrameRepair.Frame F(string n,int x,int y=0) { return new NativeHudFrameRepair.Frame(n,x,y,2,2); }
    static void Fill(byte[] data,int width,int x,int y,byte value)
    { for (int row=y;row<y+2;row++) for (int col=x;col<x+2;col++) data[row*width+col]=value; }
    public static void Main()
    {
        const int width=16,height=4;
        byte[] native=new byte[width*height],skin=new byte[native.Length];
        var frames=new[]{F("idle_v020000",0),F("idle_v020001",2),F("refill0002",4),
            F("GG_nail_bound",6),F("soulorb_flash0000",8),F("coin_break0002",10),F("empty",12)};
        foreach (var frame in frames) if(frame.name!="empty") Fill(native,width,frame.x,frame.y,200);
        Fill(skin,width,4,0,100); // Nonblank legacy refill cell is the wrong sprite.
        Fill(skin,width,8,0,150); // Real authored soul effect must survive.
        int count=NativeHudFrameRepair.Repair(skin,native,width,height,frames,p=>p>0);
        Check(count==4,"Missing legacy health animation and Godhome icon are restored");
        Check(skin[0]==200 && skin[2]==200 && skin[4]==200 && skin[6]==200,"Native HUD cells copied");
        Check(skin[8]==150,"Custom soul art preserved");
        Check(skin[10]==0,"Intentionally empty cosmetic effect preserved");
        Check(skin[12]==0,"Originally blank cells stay blank");
        Check(NativeHudFrameRepair.Repair(skin,native,width,height,frames,p=>p>0)==0,"Repair is idempotent");
        // A modern skin with valid health animation must not be overwritten.
        var modern=(byte[])native.Clone(); Fill(modern,width,0,0,33); Fill(modern,width,4,0,44);
        Check(NativeHudFrameRepair.Repair(modern,native,width,height,frames,p=>p>0)==0,"Modern sheets retain their own frames");
        Check(modern[0]==33 && modern[4]==44,"Modern custom pixels unchanged");
        // Invalid rectangles cannot partially modify an otherwise valid image.
        var bad=new byte[width*height];
        bool rejected=false;
        try { NativeHudFrameRepair.Repair(bad,native,width,height,new[]{F("GG_nail_bound",0),F("GG_soul_cap",15)},p=>p>0); }
        catch(ArgumentException) { rejected=true; }
        Check(rejected && bad[0]==0,"Validate every rectangle before changing pixels");
        Console.WriteLine("HUD_FRAME_REPAIR_TESTS_PASS");
    }
}
