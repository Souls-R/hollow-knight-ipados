using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Native implementation of the provided desktop mod's visible features.
// Registration is driven by HealthManager lifecycle/damage hooks; no repeated
// world search, Harmony patching, dynamic assembly loading or game HP writes.
public sealed class NativeEnemyHud : MonoBehaviour
{
    [Serializable] public sealed class Settings
    {
        public int bossHPThreshold=150;
        public float enemyScale=1, bossScale=1, damageScale=1, verticalOffset=1.5f;
    }
    sealed class Entry
    {
        public HealthManager health;
        public Renderer renderer;
        public Collider2D collider;
        public readonly NativeEnemyHealthState state=new NativeEnemyHealthState();
        public Vector3 position;
        public bool visible;
    }
    struct Number { public Vector3 position; public float age; public string text; public float offset; }
    static NativeEnemyHud instance;
    readonly Dictionary<int,Entry> entries=new Dictionary<int,Entry>();
    readonly List<int> removed=new List<int>();
    readonly List<Number> numbers=new List<Number>(64);
    readonly Dictionary<string,Texture2D> art=new Dictionary<string,Texture2D>();
    Camera worldCamera;
    Settings settings=new Settings();
    bool bars,numerals,paused,playing;
    GUIStyle labelStyle,buttonStyle;
    Font font;
    string customRoot;
    int sequence;
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern string NativeEnemyHud_AssetsPath();
#endif
    bool Chinese { get { return Application.systemLanguage==SystemLanguage.Chinese || Application.systemLanguage==SystemLanguage.ChineseSimplified || Application.systemLanguage==SystemLanguage.ChineseTraditional; } }
    string L(string zh,string en) { return Chinese?zh:en; }
    void Awake()
    {
        instance=this;
        bars=PlayerPrefs.GetInt("NativeEnemyBars",1)!=0;
        numerals=PlayerPrefs.GetInt("NativeDamageNumbers",1)!=0;
        customRoot=Path.Combine(Application.persistentDataPath,"HollowKnightMods/EnemyHud");
        string config=Path.Combine(customRoot,"settings.json");
        try { if (File.Exists(config) && new FileInfo(config).Length<16384) JsonUtility.FromJsonOverwrite(File.ReadAllText(config),settings); }
        catch (Exception e) { Debug.LogWarning("HK_ENEMY_HUD: settings ignored: "+e.GetType().Name); }
        settings.bossHPThreshold=Math.Max(1,settings.bossHPThreshold);
        settings.enemyScale=Scale(settings.enemyScale); settings.bossScale=Scale(settings.bossScale); settings.damageScale=Scale(settings.damageScale);
        if (float.IsNaN(settings.verticalOffset) || float.IsInfinity(settings.verticalOffset)) settings.verticalOffset=1.5f;
        settings.verticalOffset=Mathf.Clamp(settings.verticalOffset,0,5);
        string bundled="";
#if UNITY_IOS && !UNITY_EDITOR
        bundled=NativeEnemyHud_AssetsPath();
#endif
        foreach (string name in new[]{"bg","mg","ol","bossbg","bossfg","bossol","digits"}) {
            string path=Path.Combine(customRoot,name+".png");
            if (!File.Exists(path)) path=Path.Combine(bundled,name+".png");
            Texture2D texture=Load(path,name);
            if (texture) art.Add(name,texture);
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded+=SceneLoaded;
        StartCoroutine(Discover());
        Debug.Log("HK_ENEMY_HUD: loaded; bars="+bars+"; damageNumbers="+numerals+"; art="+art.Count);
    }
    static float Scale(float x) { return float.IsNaN(x)||float.IsInfinity(x)?1:Mathf.Clamp(x,.5f,3); }
    Texture2D Load(string path,string name)
    {
        Texture2D result=null;
        try {
            if (!File.Exists(path) || new FileInfo(path).Length>4*1024*1024) return null;
            // Validate dimensions before asking Unity to decode an image.
            byte[] data=File.ReadAllBytes(path);
            if (data.Length<24 || data[0]!=137 || data[1]!=80 || data[2]!=78 || data[3]!=71) return null;
            int w=(data[16]<<24)|(data[17]<<16)|(data[18]<<8)|data[19];
            int h=(data[20]<<24)|(data[21]<<16)|(data[22]<<8)|data[23];
            if (w<=0 || h<=0 || w>2048 || h>2048) return null;
            if (name=="digits" && (w%64!=0 || h%64!=0 || (w/64)*(h/64)<10)) return null;
            result=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if (!ImageConversion.LoadImage(result,data,true)) throw new InvalidDataException();
            result.name="NativeEnemyHud/"+name; result.wrapMode=TextureWrapMode.Clamp;
            return result;
        } catch(Exception e) { if(result) Destroy(result); Debug.LogWarning("HK_ENEMY_HUD: image ignored: "+name+"; "+e.GetType().Name); return null; }
    }
    void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        numbers.Clear(); entries.Clear(); worldCamera=null;
        StartCoroutine(Discover());
    }
    IEnumerator Discover()
    {
        yield return null;
        FindSceneEnemies();
        yield return new WaitForSecondsRealtime(.3f);
        FindSceneEnemies();
    }
    void FindSceneEnemies()
    {
        worldCamera=Camera.main;
        foreach(var health in Resources.FindObjectsOfTypeAll<HealthManager>()) Register(health,false);
    }
    Entry Register(HealthManager health,bool reset,int initial=-1)
    {
        if (!health || !health.gameObject.scene.IsValid() || !health.gameObject.activeInHierarchy) return null;
        int hp=initial>=0?initial:health.hp;
        if (hp<=0) return null;
        int id=health.GetInstanceID(); Entry entry;
        if (entries.TryGetValue(id,out entry)) {
            if(reset) entry.state.Spawn(hp);
            return entry;
        }
        // HealthManager also exists on scenery and special trigger objects.
        if (health.gameObject.layer!=LayerMask.NameToLayer("Enemies") &&
            !health.GetComponent<EnemyDeathEffects>() && !health.GetComponent<DamageHero>()) return null;
        entry=new Entry { health=health,renderer=health.GetComponentInChildren<Renderer>(),collider=health.GetComponent<Collider2D>() };
        entry.state.Spawn(hp); entries.Add(id,entry);
        return entry;
    }
    // Called after original OnEnable, including pooled enemy reactivation.
    public static void Spawned(HealthManager health)
    {
        if (!instance) return;
        try { instance.Register(health,true); } catch(Exception e) { Debug.LogWarning("HK_ENEMY_HUD: spawn observation: "+e.GetType().Name); }
    }
    // SubtractHealth is shared by regular hits, spells and extra/DoT damage.
    // 'damage' is its effective value AFTER the game's damageOverride rule.
    public static void Damaged(HealthManager health,int before,int damage)
    {
        if (!instance || !health) return;
        try { instance.Record(health,before,damage); } catch(Exception e) { Debug.LogWarning("HK_ENEMY_HUD: damage observation: "+e.GetType().Name); }
    }
    void Record(HealthManager health,int before,int damage)
    {
        Entry entry=Register(health,false,before);
        if (entry==null) return;
        int amount=entry.state.Hit(before,health.hp,damage);
        if (amount<=0 || !numerals) return;
        if (numbers.Count==64) numbers.RemoveAt(0);
        numbers.Add(new Number { position=health.transform.position+Vector3.up*settings.verticalOffset,
            text=amount.ToString(),offset=((sequence++%5)-2)*.12f });
    }
    void LateUpdate()
    {
        var manager=GameManager.UnsafeInstance;
        paused=manager && manager.IsGamePaused();
        playing=manager && ((int)manager.gameState==3 || (int)manager.gameState==4) && !paused;
        if (paused && InControl.InputManager.Enabled) {
            var key=Keyboard.current; var pad=Gamepad.current;
            if ((key!=null && key.f7Key.wasPressedThisFrame) || (pad!=null && pad.rightShoulder.wasPressedThisFrame)) SetBars(!bars);
            if ((key!=null && key.f8Key.wasPressedThisFrame) || (pad!=null && pad.rightStickButton.wasPressedThisFrame)) SetNumbers(!numerals);
        }
        if (!playing) return;
        if (!bars && !numerals) return;
        if (!worldCamera) worldCamera=Camera.main;
        removed.Clear();
        foreach(var pair in entries) {
            Entry entry=pair.Value;
            if (!entry.health || !entry.health.gameObject.activeInHierarchy || entry.health.hp<=0) { removed.Add(pair.Key); continue; }
            entry.state.Observe(entry.health.hp);
            entry.visible=worldCamera && (!entry.renderer || entry.renderer.isVisible);
            if (!entry.visible) continue;
            Vector3 point=entry.health.transform.position+Vector3.up*settings.verticalOffset;
            if (entry.collider && entry.collider.enabled) { var bounds=entry.collider.bounds; point=new Vector3(bounds.center.x,bounds.max.y+.4f,bounds.center.z); }
            entry.position=worldCamera.WorldToScreenPoint(point);
            entry.visible=entry.position.z>0 && entry.position.x>=0 && entry.position.x<=Screen.width && entry.position.y>=0 && entry.position.y<=Screen.height;
        }
        foreach(int id in removed) entries.Remove(id);
        for(int i=numbers.Count-1;i>=0;i--) {
            Number number=numbers[i]; number.age+=Time.deltaTime;
            if (number.age>=.8f) numbers.RemoveAt(i); else numbers[i]=number;
        }
    }
    void SetBars(bool enabled) { bars=enabled; PlayerPrefs.SetInt("NativeEnemyBars",bars?1:0); PlayerPrefs.Save(); }
    void SetNumbers(bool enabled) { numerals=enabled; if(!enabled) numbers.Clear(); PlayerPrefs.SetInt("NativeDamageNumbers",numerals?1:0); PlayerPrefs.Save(); }
    void Styles()
    {
        if(labelStyle!=null) return;
        foreach(var candidate in Resources.FindObjectsOfTypeAll<Font>()) if(candidate.name.IndexOf("NotoSerifCJK",StringComparison.OrdinalIgnoreCase)>=0) {font=candidate; break;}
        labelStyle=new GUIStyle(GUI.skin.label) {font=font,fontSize=22,alignment=TextAnchor.MiddleCenter};
        buttonStyle=new GUIStyle(GUI.skin.button) {font=font,fontSize=19};
    }
    void OnGUI()
    {
        if (!paused && (!playing || Event.current.type!=EventType.Repaint)) return;
        Styles();
        var old=GUI.matrix; Color oldColor=GUI.color;
        float scale=Math.Min(Screen.width/1000f,Screen.height/650f);
        float width=Screen.width/scale,height=Screen.height/scale;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        if (paused) {
            if (InControl.InputManager.Enabled) {
                if(GUI.Button(new Rect(width-228,80,200,44),L("怪物血条 ","Enemy bars ")+(bars?"✓":"—")+" [RB]",buttonStyle)) SetBars(!bars);
                if(GUI.Button(new Rect(width-228,132,200,44),L("伤害数字 ","Damage ")+(numerals?"✓":"—")+" [R3]",buttonStyle)) SetNumbers(!numerals);
            }
            GUI.matrix=old; return;
        }
        if (bars) {
            int bossIndex=0;
            foreach(var entry in entries.Values) {
                if(!entry.visible || !entry.state.Damaged || entry.state.Ratio<=0) continue;
                bool boss=entry.state.Maximum>=settings.bossHPThreshold;
                if(boss && bossIndex>=3) continue;
                float w=(boss?516:139)*.6f*(boss?settings.bossScale:settings.enemyScale);
                float h=(boss?48:24)*.6f*(boss?settings.bossScale:settings.enemyScale);
                var rect=boss?new Rect((width-w)/2,height-90-bossIndex++*(h+5),w,h):new Rect(entry.position.x/scale-w/2,(Screen.height-entry.position.y)/scale-h,w,h);
                Bar(rect,entry.state.Ratio,boss);
            }
        }
        if (numerals && worldCamera) foreach(var number in numbers) {
            Vector3 pos=worldCamera.WorldToScreenPoint(number.position+new Vector3(number.offset,number.age*1.5f,0));
            if(pos.z<=0) continue;
            GUI.color=new Color(1,1,1,Mathf.Clamp01((.8f-number.age)/.3f));
            Digits(number.text,pos.x/scale,(Screen.height-pos.y)/scale,settings.damageScale);
        }
        GUI.color=oldColor; GUI.matrix=old;
    }
    void Layer(Rect rect,string name,Color fallback)
    {
        Texture2D image;
        if(art.TryGetValue(name,out image)) GUI.DrawTexture(rect,image,ScaleMode.StretchToFill,true);
        else {Color previous=GUI.color; GUI.color=previous*fallback; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=previous;}
    }
    void Bar(Rect rect,float ratio,bool boss)
    {
        Layer(rect,boss?"bossbg":"bg",new Color(.08f,.08f,.08f,.8f));
        GUI.BeginGroup(new Rect(rect.x,rect.y,rect.width*ratio,rect.height));
        Layer(new Rect(0,0,rect.width,rect.height),boss?"bossfg":"mg",new Color(.85f,.12f,.12f));
        GUI.EndGroup();
        Texture2D outline;
        if(art.TryGetValue(boss?"bossol":"ol",out outline)) GUI.DrawTexture(rect,outline,ScaleMode.StretchToFill,true);
    }
    void Digits(string text,float x,float y,float size)
    {
        Texture2D atlas;
        if(!art.TryGetValue("digits",out atlas)) { GUI.Label(new Rect(x-60,y-20,120,35),text,labelStyle); return; }
        int columns=atlas.width/64;
        float tile=28*size,start=x-text.Length*tile*.5f;
        for(int i=0;i<text.Length;i++) {
            int digit=text[i]-'0';
            Rect uv=new Rect((digit%columns)*64f/atlas.width,1-(digit/columns+1)*64f/atlas.height,64f/atlas.width,64f/atlas.height);
            GUI.DrawTextureWithTexCoords(new Rect(start+i*tile,y-20,tile,tile),atlas,uv,true);
        }
    }
    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded-=SceneLoaded;
        if(instance==this) instance=null;
        foreach(var image in art.Values) if(image) Destroy(image);
    }
}
