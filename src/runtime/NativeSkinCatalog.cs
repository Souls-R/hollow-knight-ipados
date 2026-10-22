using System;
using System.Collections.Generic;
using System.IO;

// Data-only Custom Knight sheets. The original game owns all animation/UV data.
public static class NativeSkinCatalog
{
    public sealed class Sheet
    {
        public readonly string name, collection;
        public readonly int width, height;
        public Sheet(string n, string c, int w, int h) { name=n; collection=c; width=w; height=h; }
    }
    public static readonly Sheet[] Sheets = {
        new Sheet("Knight", "Knight", 4096, 4096),
        new Sheet("Sprint", "Knight Dream Gate Cln", 512, 1024),
        new Sheet("HUD", "HUD Cln", 2048, 2048),
        new Sheet("VS", "Spell Effects", 2048, 2048),
        new Sheet("Wraiths", "Spell Effects 2", 2048, 2048),
        new Sheet("VoidSpells", "Spell Effects Neutral", 2048, 2048),
        new Sheet("Unn", "Knight Slug Cln", 1024, 1024),
        new Sheet("Geo", "Geo", 256, 512),
        new Sheet("Shade", "Hollow Shade Cln", 2048, 2048),
        new Sheet("Hatchling", "Knight Hatchling Cln", 1024, 1024),
        new Sheet("Fluke", "Fluke Spell Cln", 1024, 1024),
        new Sheet("Shield", "Orbit Shield Cln", 1024, 1024),
        new Sheet("Baldur", "Charm Blocker Cln", 1024, 1024),
        new Sheet("Grimm", "Grimmchild Cln", 2048, 2048),
        new Sheet("Weaver", "Weaverling Cln", 512, 1024),
        new Sheet("Liquid", "Water_effects", 512, 1024),
        new Sheet("Compass", "Compass Icon Cln", 256, 512),
        new Sheet("OrbFull", "", 130, 125),
        new Sheet("Cloak", "@Abyss_10/higher_being/Dish Plat/Knight Dummy", 1024, 1024),
        new Sheet("Shriek", "@Abyss_12/Scream 2 Get/Cutscene Knight", 512, 512),
        new Sheet("Wings", "@Abyss_21/Shiny Item DJ/Knight Cutscene", 1024, 1024),
        new Sheet("Quirrel", "@Crossroads_50/Quirrel Lakeside/Sit Region/Knight Sit", 1024, 2048),
        new Sheet("Hornet", "@Deepnest_East_12/Hornet Blizzard Return Scene", 2048, 2048),
        new Sheet("Webbed", "@Deepnest_Spider_Town/RestBench Spider/Webbed Knight", 1024, 1024),
        new Sheet("Birthplace", "@Dream_Abyss/End Cutscene/Dummy", 1024, 2048),
        new Sheet("DreamArrival", "@GG_Vengefly/Boss Scene Controller/Dream Entry/Knight Dream Arrival", 1024, 1024),
        new Sheet("Dreamnail", "@RestingGrounds_07/Dream Moth/Knight Dummy", 1024, 1024)
    };
    public sealed class Pack
    {
        public string id, directory, name, author = "";
        public readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
    public static List<Pack> Scan(string root)
    {
        var result = new List<Pack>();
        if (!Directory.Exists(root)) return result;
        foreach (string directory in Directory.GetDirectories(root))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var pack = new Pack { id=Path.GetFileName(directory), directory=directory, name=Path.GetFileName(directory) };
            bool duplicate = false;
            foreach (string file in Directory.GetFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                    !string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = Path.GetFileNameWithoutExtension(file);
                foreach (var sheet in Sheets)
                    if (string.Equals(sheet.name, stem, StringComparison.OrdinalIgnoreCase)) {
                        if (pack.files.ContainsKey(sheet.name)) duplicate = true;
                        else pack.files.Add(sheet.name, file);
                    }
            }
            if (!duplicate && pack.files.ContainsKey("Knight")) result.Add(pack);
        }
        result.Sort((a,b) => string.Compare(a.id,b.id,StringComparison.OrdinalIgnoreCase));
        return result;
    }
    public static bool ValidSize(Sheet sheet, int width, int height)
    {
        return (width == sheet.width && height == sheet.height) ||
               (width == sheet.width*2 && height == sheet.height*2);
    }
    public static byte[] ReadPng(string path, Sheet sheet)
    {
        var info = new FileInfo(path);
        if (info.Length < 24 || info.Length > 80L*1024*1024) throw new InvalidDataException(sheet.name+": file size");
        // Read the header before allocating a decompressed Unity texture.
        byte[] header = new byte[24];
        using (var stream = File.OpenRead(path)) {
            int offset = 0, count;
            while (offset < header.Length && (count=stream.Read(header,offset,header.Length-offset)) > 0) offset+=count;
            if (offset != header.Length) throw new InvalidDataException(sheet.name+": PNG header");
        }
        byte[] signature = {137,80,78,71,13,10,26,10};
        for (int i=0;i<8;i++) if (header[i] != signature[i]) throw new InvalidDataException(sheet.name+": PNG signature");
        if (header[12]!=73 || header[13]!=72 || header[14]!=68 || header[15]!=82)
            throw new InvalidDataException(sheet.name+": PNG IHDR");
        int width = (header[16]<<24)|(header[17]<<16)|(header[18]<<8)|header[19];
        int height = (header[20]<<24)|(header[21]<<16)|(header[22]<<8)|header[23];
        if (!ValidSize(sheet,width,height)) throw new InvalidDataException(sheet.name+": incompatible dimensions");
        return File.ReadAllBytes(path);
    }
}
