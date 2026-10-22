using System;
using System.IO;

public static class SkinCatalogTests
{
    static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected rejection"); }
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"hk-skins-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            string a=Path.Combine(root,"a"),b=Path.Combine(root,"b");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            byte[] png=new byte[24]; byte[] magic={137,80,78,71,13,10,26,10}; Array.Copy(magic,png,8);
            png[12]=73; png[13]=72; png[14]=68; png[15]=82;
            png[18]=16; png[22]=16; // 4096 x 4096
            string file=Path.Combine(a,"kNiGhT.PNG"); File.WriteAllBytes(file,png);
            File.WriteAllText(Path.Combine(b,"Sprint.png"),"no Knight");
            var packs=NativeSkinCatalog.Scan(root);
            Check(packs.Count==1 && packs[0].id=="a","Only folders containing Knight are offered");
            Check(packs[0].files["Knight"]==file,"Case-insensitive canonical sheet names");
            Check(NativeSkinCatalog.ReadPng(file,NativeSkinCatalog.Sheets[0]).Length==24,"Valid header");
            png[18]=8; File.WriteAllBytes(file,png);
            Reject(()=>NativeSkinCatalog.ReadPng(file,NativeSkinCatalog.Sheets[0]));
            png[0]=0; File.WriteAllBytes(file,png);
            Reject(()=>NativeSkinCatalog.ReadPng(file,NativeSkinCatalog.Sheets[0]));
            Directory.Delete(a,true);
            Check(NativeSkinCatalog.Scan(root).Count==0,"Removing the selected folder removes its catalog entry");
            Check(NativeSkinCatalog.ValidSize(NativeSkinCatalog.Sheets[0],8192,8192),"2x sheets");
            Check(!NativeSkinCatalog.ValidSize(NativeSkinCatalog.Sheets[0],8192,4096),"No stretched atlases");
            Console.WriteLine("SKIN_CATALOG_TESTS_PASS");
        } finally { Directory.Delete(root,true); }
    }
}
