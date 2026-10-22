using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Swap material texture references, never rewrite the purchased resource pack.
// Discovery runs on scene transitions and selection, not in a gameplay poll.
[DefaultExecutionOrder(-10000)]
public sealed class NativeSkins : MonoBehaviour
{
    [Serializable] sealed class Metadata { public string name, author, source; }
    readonly Dictionary<Texture, string> originals = new Dictionary<Texture, string>();
    Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    readonly Dictionary<SpriteRenderer, Sprite> orbOriginals = new Dictionary<SpriteRenderer, Sprite>();
    readonly Dictionary<Sprite, Sprite> orbSprites = new Dictionary<Sprite, Sprite>();
    readonly Dictionary<SpriteRenderer, bool> pulseOriginals = new Dictionary<SpriteRenderer, bool>();
    readonly List<Behaviour> blockedEvents = new List<Behaviour>();
    List<NativeSkinCatalog.Pack> packs = new List<NativeSkinCatalog.Pack>();
    string root, selected = "", status = "";
    bool paused, menu, inputHeld, previousInput, busy, refreshQueued, pendingReapply;
    int cursor;
    float releaseAfter;
    GUIStyle titleStyle, labelStyle, buttonStyle;
    Font font;
    Vector2 scroll;
    bool Chinese { get { return Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.ChineseTraditional; } }
    string L(string zh,string en) { return Chinese ? zh : en; }

    void Awake()
    {
        root = Path.Combine(Application.persistentDataPath,"HollowKnightSkins");
        Directory.CreateDirectory(root);
        selected = PlayerPrefs.GetString("NativeSkin", "");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
        Refresh();
        StartCoroutine(ApplySaved());
    }
    IEnumerator ApplySaved()
    {
        yield return null;
        if (selected.Length == 0) yield break;
        var pack = packs.Find(p=>p.id==selected);
        if (pack == null) { selected=""; PlayerPrefs.DeleteKey("NativeSkin"); PlayerPrefs.Save(); yield break; }
        yield return Select(pack);
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Close();
        if (!refreshQueued) StartCoroutine(SceneRefresh());
    }
    IEnumerator SceneRefresh()
    {
        refreshQueued=true;
        // Includes objects instantiated by Start/coroutines after sceneLoaded.
        yield return null;
        if (!busy) Reapply(); else pendingReapply=true;
        yield return new WaitForSecondsRealtime(.5f);
        if (!busy) Reapply(); else pendingReapply=true;
        refreshQueued=false;
    }
    void Refresh()
    {
        try {
            packs=NativeSkinCatalog.Scan(root);
            foreach (var pack in packs) {
                string meta=Path.Combine(pack.directory,"metadata.json");
                if (File.Exists(meta) && new FileInfo(meta).Length < 16384) {
                    try { var m=JsonUtility.FromJson<Metadata>(File.ReadAllText(meta));
                        if (m!=null) { if (!string.IsNullOrWhiteSpace(m.name)) pack.name=m.name; pack.author=m.author ?? ""; }
                    } catch { }
                }
            }
            cursor=Mathf.Clamp(cursor,0,packs.Count);
        } catch (Exception e) { status=L("无法读取皮肤目录：","Cannot read skins: ")+e.GetType().Name; }
    }
    IEnumerator Select(NativeSkinCatalog.Pack pack)
    {
        if (busy) yield break;
        busy=true;
        status=L("正在载入…","Loading…");
        var next=new Dictionary<string,Texture2D>();
        string error=null;
        foreach (var sheet in NativeSkinCatalog.Sheets) {
            string path;
            if (pack==null || !pack.files.TryGetValue(sheet.name,out path)) continue;
            Texture2D tex=null;
            try {
                byte[] png=NativeSkinCatalog.ReadPng(path,sheet);
                tex=new Texture2D(2,2,TextureFormat.RGBA32,false);
                // HUD cells are checked against the current game before the
                // CPU copy is discarded; other sheets remain GPU-only.
                if (!ImageConversion.LoadImage(tex,png,sheet.name!="HUD")) throw new InvalidDataException(sheet.name+": decode failed");
                tex.name="NativeSkin/"+sheet.name;
                tex.wrapMode=TextureWrapMode.Clamp;
                tex.filterMode=FilterMode.Bilinear;
                next.Add(sheet.name,tex);
            } catch (Exception e) { if (tex) Destroy(tex); error=e.Message; break; }
            // Bound each frame's decoder work. The previous skin stays active.
            yield return null;
        }
        if (error!=null) {
            foreach (var tex in next.Values) Destroy(tex);
            status=L("载入失败，保留当前皮肤：","Load failed; kept current skin: ")+error;
            Debug.LogWarning("HK_SKIN: load rejected: "+error);
        } else {
            // Restore every old reference before releasing its GPU textures.
            RestoreOrbs();
            ReplaceMaterials(null);
            foreach (var sprite in orbSprites.Values) if (sprite) Destroy(sprite);
            orbSprites.Clear();
            var old=textures;
            textures=next;
            selected=pack==null ? "" : pack.id;
            Reapply();
            foreach (var tex in old.Values) Destroy(tex);
            PlayerPrefs.SetString("NativeSkin",selected); PlayerPrefs.Save();
            status=L("已应用：","Applied: ")+(pack==null?L("原版","Original"):pack.name);
            Debug.Log("HK_SKIN: selected="+(pack==null?"original":pack.id)+"; sheets="+textures.Count+"; originals="+originals.Count);
        }
        busy=false;
        if (pendingReapply) { pendingReapply=false; Reapply(); }
    }
    void Discover()
    {
        foreach (var collection in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>()) {
            Texture2D hud;
            if (collection.spriteCollectionName=="HUD Cln" && textures.TryGetValue("HUD",out hud) && hud.isReadable) {
                try {
                    int repaired=NativeHudCompatibility.Prepare(hud,collection);
                    Debug.Log("HK_SKIN: HUD native-frame fallback="+repaired+"; atlas="+hud.width+"x"+hud.height);
                } catch (Exception error) {
                    // Keep gameplay indicators readable even if conversion
                    // cannot run on this device. Other skin sheets still apply.
                    Debug.LogWarning("HK_SKIN: HUD repair unavailable; keeping native HUD: "+error.GetType().Name);
                    textures.Remove("HUD"); Destroy(hud);
                }
            }
            foreach (var sheet in NativeSkinCatalog.Sheets) {
                if (sheet.collection.Length==0 || collection.spriteCollectionName!=sheet.collection) continue;
                if (collection.materials==null) continue;
                foreach (var mat in collection.materials) {
                    if (!mat || !mat.HasProperty("_MainTex")) continue;
                    Register(mat,sheet);
                }
            }
        }
        // Scene-specific acquisition/cinematic dummies use separate atlases.
        foreach (var sprite in Resources.FindObjectsOfTypeAll<tk2dBaseSprite>()) {
            if (!sprite.gameObject.scene.IsValid()) continue;
            foreach (var sheet in NativeSkinCatalog.Sheets) {
                if (!sheet.collection.StartsWith("@",StringComparison.Ordinal)) continue;
                int slash=sheet.collection.IndexOf('/');
                if (sprite.gameObject.scene.name!=sheet.collection.Substring(1,slash-1)) continue;
                string[] path=sheet.collection.Substring(slash+1).Split('/');
                Transform node=sprite.transform;
                bool matches=true;
                for (int i=path.Length-1;i>=0;i--) {
                    if (!node || node.name!=path[i]) { matches=false; break; }
                    node=node.parent;
                }
                if (matches) {
                    // Some inactive cutscene dummies initialize after Start.
                    try {
                        var definition=sprite.GetCurrentSpriteDef();
                        if (definition!=null) Register(definition.material,sheet);
                    } catch (NullReferenceException) { }
                      catch (IndexOutOfRangeException) { }
                }
            }
        }
    }
    void Register(Material mat,NativeSkinCatalog.Sheet sheet)
    {
        if (!mat || !mat.HasProperty("_MainTex")) return;
        Texture tex=mat.mainTexture;
        if (tex && NativeSkinCatalog.ValidSize(sheet,tex.width,tex.height) &&
            !textures.ContainsValue(tex as Texture2D) && !originals.ContainsKey(tex)) originals.Add(tex,sheet.name);
    }
    void ReplaceMaterials(Dictionary<string,Texture2D> replacement)
    {
        var customToOriginal=new Dictionary<Texture,Texture>();
        foreach (var pair in originals) {
            Texture2D custom;
            if (pair.Key && textures.TryGetValue(pair.Value,out custom)) customToOriginal[custom]=pair.Key;
        }
        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>()) {
            if (!mat || !mat.HasProperty("_MainTex")) continue;
            Texture current=mat.mainTexture, original;
            string sheet;
            if (!current) continue;
            if (originals.TryGetValue(current,out sheet)) original=current;
            else if (customToOriginal.TryGetValue(current,out original)) sheet=originals[original];
            else continue;
            Texture2D custom;
            Texture target=replacement!=null && replacement.TryGetValue(sheet,out custom) &&
                !(sheet=="HUD" && custom.isReadable) ? custom : original;
            if (target && current!=target) mat.mainTexture=target;
        }
    }
    void Reapply()
    {
        if (textures.Count==0) return;
        Discover();
        ReplaceMaterials(textures);
        Texture2D orb;
        if (!textures.TryGetValue("OrbFull",out orb)) return;
        foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>()) {
            if (renderer.name=="Pulse Sprite" && renderer.gameObject.scene.IsValid() && !pulseOriginals.ContainsKey(renderer)) {
                pulseOriginals.Add(renderer,renderer.enabled);
                renderer.enabled=false;
            }
            if (renderer.name!="Orb Full" || !renderer.gameObject.scene.IsValid() || !renderer.sprite || orbOriginals.ContainsKey(renderer)) continue;
            Sprite original=renderer.sprite, sprite;
            if (!orbSprites.TryGetValue(original,out sprite)) {
                Vector2 pivot=new Vector2(original.pivot.x/original.rect.width,original.pivot.y/original.rect.height);
                float scale=orb.width/original.rect.width;
                sprite=Sprite.Create(orb,new Rect(0,0,orb.width,orb.height),pivot,original.pixelsPerUnit*scale,0,SpriteMeshType.FullRect,original.border*scale);
                orbSprites.Add(original,sprite);
            }
            orbOriginals.Add(renderer,original);
            renderer.sprite=sprite;
        }
    }
    void RestoreOrbs()
    {
        foreach (var pair in pulseOriginals) if (pair.Key) pair.Key.enabled=pair.Value;
        pulseOriginals.Clear();
        foreach (var pair in orbOriginals) if (pair.Key && pair.Value) pair.Key.sprite=pair.Value;
        orbOriginals.Clear();
    }
    void Open()
    {
        if (menu || inputHeld) return;
        Refresh(); menu=true;
        previousInput=InControl.InputManager.Enabled;
        InControl.InputManager.Enabled=false;
        inputHeld=true;
        // Keep EventSystem.current alive: original MenuSelectable validation
        // coroutines still access it while the custom selector is open.
        foreach (var events in Resources.FindObjectsOfTypeAll<BaseInputModule>())
            if (events.enabled && events.gameObject.scene.IsValid()) { blockedEvents.Add(events); events.enabled=false; }
        int index=packs.FindIndex(p=>p.id==selected);
        cursor=index+1;
        if (selected.Length>0 && index<0 && !busy) StartCoroutine(Select(null));
    }
    void Close() { if (!menu) return; menu=false; releaseAfter=Time.unscaledTime+.15f; }
    void ReleaseInput()
    {
        if (!inputHeld) return;
        InControl.InputManager.Enabled=previousInput;
        foreach (var events in blockedEvents) if (events) events.enabled=true;
        blockedEvents.Clear(); inputHeld=false;
    }
    bool Neutral()
    {
        var key=Keyboard.current;
        if (key!=null && key.anyKey.isPressed) return false;
        foreach (var pad in Gamepad.all)
            if (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.startButton.isPressed || pad.leftShoulder.isPressed || pad.dpad.ReadValue().sqrMagnitude>.1f || pad.leftStick.ReadValue().sqrMagnitude>.1f) return false;
        return true;
    }
    void Update()
    {
        // Find an existing manager; do not invoke its singleton during intro.
        paused=Time.timeScale==0f && GameManager.UnsafeInstance!=null && GameManager.UnsafeInstance.IsGamePaused();
        if (!paused && menu) Close();
        if (inputHeld && !menu && Time.unscaledTime>=releaseAfter && Neutral()) ReleaseInput();
        if (!paused || (inputHeld && !menu)) return;
        var key=Keyboard.current; var pad=Gamepad.current;
        if (!menu) {
            if ((key!=null && key.f6Key.wasPressedThisFrame) || (pad!=null && pad.leftShoulder.wasPressedThisFrame)) Open();
            return;
        }
        if (busy) return;
        if ((key!=null && key.escapeKey.wasPressedThisFrame) || (pad!=null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))) { Close(); return; }
        int move=0;
        if ((key!=null && key.upArrowKey.wasPressedThisFrame) || (pad!=null && pad.dpad.up.wasPressedThisFrame)) move=-1;
        if ((key!=null && key.downArrowKey.wasPressedThisFrame) || (pad!=null && pad.dpad.down.wasPressedThisFrame)) move=1;
        if (move!=0) { cursor=(cursor+move+packs.Count+1)%(packs.Count+1); scroll.y=Math.Max(0,(cursor-2)*58); }
        if ((key!=null && key.enterKey.wasPressedThisFrame) || (pad!=null && pad.buttonSouth.wasPressedThisFrame))
            StartCoroutine(Select(cursor==0?null:packs[cursor-1]));
        if (key!=null && key.rKey.wasPressedThisFrame) Refresh();
    }
    void OnGUI()
    {
        if (!paused) return;
        if (titleStyle==null) {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Font>())
                if (candidate.name.IndexOf("NotoSerifCJK",StringComparison.OrdinalIgnoreCase)>=0) { font=candidate; break; }
            if (!font && Chinese) font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Heiti SC"},24);
            titleStyle=new GUIStyle(GUI.skin.label) {font=font,fontSize=28,alignment=TextAnchor.MiddleCenter};
            labelStyle=new GUIStyle(GUI.skin.label) {font=font,fontSize=18,wordWrap=true};
            buttonStyle=new GUIStyle(GUI.skin.button) {font=font,fontSize=22};
        }
        Matrix4x4 old=GUI.matrix;
        int oldDepth=GUI.depth;
        float scale=Math.Min(Screen.width/1000f,Screen.height/650f);
        float width=Screen.width/scale, height=Screen.height/scale;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        if (!menu) {
            if (!inputHeld && GUI.Button(new Rect(width-228,24,200,48),L("皮肤  [LB / F6]","Skins  [LB / F6]"),buttonStyle)) Open();
            GUI.matrix=old; return;
        }
        GUI.depth=-1000;
        GUI.Box(new Rect(0,0,width,height),GUIContent.none);
        float x=(width-760)/2, y=(height-590)/2;
        GUI.Box(new Rect(x,y,760,590),GUIContent.none);
        GUI.Label(new Rect(x+20,y+10,720,45),L("选择皮肤","Choose skin"),titleStyle);
        GUI.Label(new Rect(x+30,y+58,700,48),L("方向键选择 · A / 回车应用 · B / Esc 返回","D-pad to select · A / Enter to apply · B / Esc to close"),labelStyle);
        GUI.enabled=!busy;
        scroll=GUI.BeginScrollView(new Rect(x+25,y+110,710,340),scroll,new Rect(0,0,680,Math.Max(340,(packs.Count+1)*58)));
        for (int i=0;i<=packs.Count;i++) {
            string id=i==0?"":packs[i-1].id;
            string name=i==0?L("原版","Original"):packs[i-1].name;
            string author=i==0?"":packs[i-1].author;
            string text=(cursor==i?"> ":"")+(id==selected?"✓ ":"")+name+(author.Length>0?"  —  "+author:"");
            if (GUI.Button(new Rect(0,i*58,670,52),text,buttonStyle)) { cursor=i; StartCoroutine(Select(i==0?null:packs[i-1])); }
        }
        GUI.EndScrollView();
        GUI.Label(new Rect(x+30,y+460,700,62),status.Length>0?status:L("添加或删除文件夹后，点“刷新列表”。","After adding/removing folders, refresh the list."),labelStyle);
        if (GUI.Button(new Rect(x+30,y+530,330,44),L("刷新列表","Refresh list"),buttonStyle)) {
            Refresh();
            if (selected.Length>0 && !packs.Exists(p=>p.id==selected)) StartCoroutine(Select(null));
        }
        if (GUI.Button(new Rect(x+400,y+530,330,44),L("返回游戏菜单","Back"),buttonStyle)) Close();
        GUI.enabled=true; GUI.matrix=old; GUI.depth=oldDepth;
    }
    void OnApplicationFocus(bool focused) { if (!focused) { Close(); ReleaseInput(); } }
    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded-=SceneLoaded;
        ReleaseInput(); RestoreOrbs(); ReplaceMaterials(null);
        foreach (var sprite in orbSprites.Values) if (sprite) Destroy(sprite);
        foreach (var tex in textures.Values) if (tex) Destroy(tex);
    }
}
