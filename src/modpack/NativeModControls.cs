using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NativeModpack
{
    public sealed class ModControls:MonoBehaviour
    {
        sealed class Command {public string name,category;public MethodInfo method;}
        readonly List<Command> commands=new List<Command>();
        readonly List<Command> visible=new List<Command>();
        bool open,held,previousInput;
        EventSystem events;bool previousNavigation;GameObject blocker;
        int invokedFrame=-1;
        bool searchFocused;
        float releaseAfter;
        int cursor;
        string search="",message="";
        Vector2 scroll;
        GUIStyle button,label,title;
        void Awake()
        {
            foreach(var method in typeof(DebugMod.BindableFunctions).GetMethods(BindingFlags.Public|BindingFlags.Static))
            {
                var attribute=Attribute.GetCustomAttribute(method,typeof(DebugMod.BindableMethod)) as DebugMod.BindableMethod;
                if(attribute==null)continue;
                if(method.GetParameters().Length!=0)throw new InvalidOperationException("Debug command needs arguments: "+method.Name);
                commands.Add(new Command{name=attribute.name,category=attribute.category,method=method});
            }
            commands.Sort((a,b)=>string.CompareOrdinal(a.category+"/"+a.name,b.category+"/"+b.name));
            Filter();
            Debug.Log("[NativeModpack] Debug touch commands="+commands.Count);
        }
        void Filter()
        {
            visible.Clear();
            foreach(var command in commands)if((command.category+" "+command.name).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)visible.Add(command);
            cursor=Mathf.Clamp(cursor,0,Math.Max(0,visible.Count-1));
        }
        void Open()
        {
            if(held||!InControl.InputManager.Enabled)return;
            var registered=DebugPresentation.Commands();
            if(registered.Count>0){commands.Clear();foreach(var pair in registered)commands.Add(new Command{name=pair.Key,category=(string)pair.Value.First,method=(MethodInfo)pair.Value.Second});commands.Sort((a,b)=>string.CompareOrdinal(a.category+"/"+a.name,b.category+"/"+b.name));Filter();}
            previousInput=InControl.InputManager.Enabled;InControl.InputManager.Enabled=false;
            events=EventSystem.current;if(events){previousNavigation=events.sendNavigationEvents;events.sendNavigationEvents=false;}
            if(!blocker){blocker=new GameObject("DebugModal",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));blocker.transform.SetParent(transform,false);var canvas=blocker.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;var shield=new GameObject("InputShield",typeof(RectTransform),typeof(Image));shield.transform.SetParent(blocker.transform,false);var rect=(RectTransform)shield.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;shield.GetComponent<Image>().color=Color.clear;}
            blocker.SetActive(true);DebugPresentation.CommandMenuOpen=true;
            held=true;open=true;message="";
        }
        void Close() {if(!open)return;open=false;DebugPresentation.CommandMenuOpen=false;releaseAfter=Time.unscaledTime+.15f;}
        void Release() {if(!held)return;InControl.InputManager.Enabled=previousInput;if(events)events.sendNavigationEvents=previousNavigation;if(blocker)blocker.SetActive(false);DebugPresentation.CommandMenuOpen=false;held=false;}
        bool ButtonsReleased(){var p=Gamepad.current;var k=Keyboard.current;return (p==null||!(p.buttonSouth.isPressed||p.buttonEast.isPressed||p.startButton.isPressed||p.leftStickButton.isPressed||p.dpad.IsPressed()))&&(k==null||!(k.enterKey.isPressed||k.escapeKey.isPressed||k.f12Key.isPressed));}
        void OnDestroy() {Release();}
        void Invoke(int index)
        {
            if(index<0||index>=visible.Count||invokedFrame==Time.frameCount)return;
            invokedFrame=Time.frameCount;
            try {visible[index].method.Invoke(null,null);message=visible[index].name+" ✓";}
            catch(Exception error) {message=(error.InnerException??error).Message;Debug.LogError("[NativeModpack] Debug command "+visible[index].name+": "+error);}
        }
        void Update()
        {
            if(!open&&held&&Time.unscaledTime>=releaseAfter&&ButtonsReleased())Release();
            var manager=GameManager.UnsafeInstance;
            if(!manager||!manager.IsGamePaused()){Close();return;}
            var pad=Gamepad.current;var keyboard=Keyboard.current;
            if(!open){if((pad!=null&&pad.leftStickButton.wasPressedThisFrame)||(keyboard!=null&&keyboard.f12Key.wasPressedThisFrame))Open();return;}
            if((pad!=null&&(pad.buttonEast.wasPressedThisFrame||pad.startButton.wasPressedThisFrame))||(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)){Close();return;}
            int move=0;
            bool typing=searchFocused;
            if((pad!=null&&pad.dpad.up.wasPressedThisFrame)||(!typing&&keyboard!=null&&keyboard.upArrowKey.wasPressedThisFrame))move=-1;
            if((pad!=null&&pad.dpad.down.wasPressedThisFrame)||(!typing&&keyboard!=null&&keyboard.downArrowKey.wasPressedThisFrame))move=1;
            if(move!=0&&visible.Count>0){cursor=(cursor+move+visible.Count)%visible.Count;scroll.y=Math.Max(0,(cursor-2)*52);}
            if((pad!=null&&pad.buttonSouth.wasPressedThisFrame)||(!typing&&keyboard!=null&&keyboard.enterKey.wasPressedThisFrame))Invoke(cursor);
        }
        void OnGUI()
        {
            var manager=GameManager.UnsafeInstance;if(!manager||!manager.IsGamePaused())return;
            if(button==null)
            {
                Font font=null;
                foreach(var item in Resources.FindObjectsOfTypeAll<Font>())if(item.name.IndexOf("NotoSerifCJK",StringComparison.OrdinalIgnoreCase)>=0){font=item;break;}
                if(!font)font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Heiti SC"},22);
                button=new GUIStyle(GUI.skin.button){font=font,fontSize=21};
                label=new GUIStyle(GUI.skin.label){font=font,fontSize=18,wordWrap=true};
                title=new GUIStyle(label){fontSize=27,alignment=TextAnchor.MiddleCenter};
            }
            var matrix=GUI.matrix;int depth=GUI.depth;
            float scale=Math.Min(Screen.width/1000f,Screen.height/650f),width=Screen.width/scale,height=Screen.height/scale;
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            try
            {
                if(!open){if(!held&&InControl.InputManager.Enabled&&GUI.Button(new Rect(width-228,184,200,44),"Debug [L3 / F12]",button))Open();return;}
                GUI.depth=-1100;
                GUI.Box(new Rect(0,0,width,height),GUIContent.none);
                float x=(width-800)/2,y=(height-610)/2;
                GUI.Box(new Rect(x,y,800,610),GUIContent.none);
                GUI.Label(new Rect(x+20,y+8,760,40),"Debug · "+commands.Count+" 个命令",title);
                GUI.Label(new Rect(x+25,y+52,750,30),"方向键选择 · A 执行 · B 返回；执行后可返回游戏查看效果",label);
                var evt=Event.current;if(GUI.GetNameOfFocusedControl()!="DebugSearch"&&(evt.type==EventType.KeyDown||evt.type==EventType.KeyUp)&&(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.Space||evt.keyCode==KeyCode.UpArrow||evt.keyCode==KeyCode.DownArrow))evt.Use();
                GUI.SetNextControlName("DebugSearch");string next=GUI.TextField(new Rect(x+25,y+88,750,38),search);
                searchFocused=GUI.GetNameOfFocusedControl()=="DebugSearch";
                if(next!=search){search=next;Filter();scroll=Vector2.zero;}
                scroll=GUI.BeginScrollView(new Rect(x+25,y+135,750,300),scroll,new Rect(0,0,720,Math.Max(300,visible.Count*52)));
                for(int i=0;i<visible.Count;i++)if(GUI.Button(new Rect(0,i*52,710,47),(i==cursor?"> ":"")+visible[i].category+" · "+visible[i].name,button)){cursor=i;Invoke(i);}
                GUI.EndScrollView();
                if(GUI.Button(new Rect(x+25,y+449,365,42),"手柄调试快捷键："+(LegacyInput.ControllerShortcuts?"开":"关"),button))LegacyInput.ControllerShortcuts=!LegacyInput.ControllerShortcuts;
                if(GUI.Button(new Rect(x+410,y+449,365,42),"清除手柄调试绑定",button))message="已清除 "+DebugPresentation.ClearControllerBindings()+" 个手柄绑定";
                if(GUI.Button(new Rect(x+25,y+499,365,42),"恢复正常时间 / 关闭调试鼠标",button)){DebugPresentation.RestoreTime();message="已恢复正常时间";}
                if(GUI.Button(new Rect(x+410,y+499,365,42),"返回游戏菜单",button))Close();
                GUI.Label(new Rect(x+25,y+546,750,52),message,label);
            }
            finally {GUI.matrix=matrix;GUI.depth=depth;}
        }
    }
}
