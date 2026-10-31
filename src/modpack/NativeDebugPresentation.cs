using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NativeModpack
{
    public static class DebugPresentation
    {
        const BindingFlags Fields=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly Type debug=typeof(DebugMod.DebugMod),keyPanel=typeof(DebugMod.KeyBindPanel);
        static Dictionary<string,string> pageOwners=new Dictionary<string,string>();
        static Font font;
        public static bool CommandMenuOpen;
        static Font CjkFont()
        {
            if(font)return font;
            foreach(string name in new[]{"PingFang SC","PingFangSC-Regular","Heiti SC","Arial Unicode MS","Arial"})
            {
                var candidate=Font.CreateDynamicFontFromOSFont(name,16);
                if(!candidate)continue;
                candidate.RequestCharactersInTexture("空洞骑士调试冻结游戏时间",16);
                if(candidate.HasCharacter('冻')){font=candidate;break;}
                UnityEngine.Object.Destroy(candidate);
            }
            if(font){font.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Debug.Log("[NativeModpack] Debug CJK font="+font.name);}
            return font;
        }
        public static void ConfigureFonts(DebugMod.GUIController controller)
        {var chosen=CjkFont();if(chosen){controller.arial=chosen;controller.trajanNormal=chosen;controller.trajanBold=chosen;}}
        public static void ConfigureText(GameObject owner)
        {
            if(!owner)return;var text=owner.GetComponent<Text>();if(!text)return;
            var chosen=CjkFont();if(chosen)text.font=chosen;
            text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
            if(text.fontSize==15&&chosen&&chosen.lineHeight>0)
                text.lineSpacing=Mathf.Clamp(17.5f*chosen.fontSize/(chosen.lineHeight*text.fontSize),.6f,1.5f);
        }
        public static void ConfigureButton(Button button)
        {if(!button)return;var nav=button.navigation;nav.mode=Navigation.Mode.None;button.navigation=nav;}
        public static bool BeforeUpdate(DebugMod.GUIController controller)
        {
            var manager=GameManager.UnsafeInstance;
            bool active=manager&&manager.IsGameplayScene()&&!CommandMenuOpen&&InControl.InputManager.Enabled;
            if(controller.canvas)
            {
                var es=EventSystem.current;var selected=es?es.currentSelectedGameObject:null;
                if(selected&&selected.transform.IsChildOf(controller.canvas.transform))es.SetSelectedGameObject(null);
                if(controller.canvas.activeSelf!=active)controller.canvas.SetActive(active);
            }
            return active;
        }
        public static bool CanCaptureBinding()
        {
            if(LegacyInput.HardwareKeyboardPresent||LegacyInput.ControllerShortcuts)return true;
            DebugMod.Console.AddLine("请连接键盘录入；手柄命令可在暂停后的 Debug 列表中执行。");
            return false;
        }
        public static void PaginateHelp()
        {
            var pages=(Dictionary<string,List<string>>)keyPanel.GetField("bindPages",Fields).GetValue(null);
            var keys=(List<string>)keyPanel.GetField("pageKeys",Fields).GetValue(null);
            if(pages==null||keys==null)return;
            var pageField=keyPanel.GetField("page",Fields);
            var plan=BindingPages.Create(pages,keys,pageOwners,(int)pageField.GetValue(null),11);
            pageOwners=plan.owners;keyPanel.GetField("bindPages",Fields).SetValue(null,plan.pages);keyPanel.GetField("pageKeys",Fields).SetValue(null,plan.keys);pageField.SetValue(null,plan.selected);
            var panel=(DebugMod.Canvas.CanvasPanel)keyPanel.GetField("panel",Fields).GetValue(null);
            if(panel!=null&&plan.keys.Count>0){panel.GetText("Category").UpdateText(plan.keys[plan.selected]);panel.GetButton("Page").UpdateText((plan.selected+1)+" / "+plan.keys.Count);}
        }
        public static string DecorateHelp(string value)
        {return LegacyInput.ControllerShortcuts?value:value.Replace("Joystick","[已停用] Joystick");}
        public static Dictionary<string,DebugMod.Pair> Commands()
        {
            var result=new Dictionary<string,DebugMod.Pair>();
            foreach(string name in new[]{"bindMethods","AdditionalBindMenthods"})
            {
                var items=(Dictionary<string,DebugMod.Pair>)debug.GetField(name,Fields).GetValue(null);
                if(items!=null)foreach(var pair in items)result[pair.Key]=pair.Value;
            }
            return result;
        }
        public static void RunHelpCommand(string name)
        {
            int index;if(!name.StartsWith("run")||!int.TryParse(name.Substring(3),out index))return;
            var pages=(Dictionary<string,List<string>>)keyPanel.GetField("bindPages",Fields).GetValue(null);
            var keys=(List<string>)keyPanel.GetField("pageKeys",Fields).GetValue(null);
            int page=(int)keyPanel.GetField("page",Fields).GetValue(null);
            if(page<0||page>=keys.Count||index<0||index>=pages[keys[page]].Count)return;
            DebugMod.Pair pair;if(Commands().TryGetValue(pages[keys[page]][index],out pair))((MethodInfo)pair.Second).Invoke(null,null);
        }
        public static int ClearControllerBindings()
        {
            var bindings=DebugMod.DebugMod.settings.binds;var removed=new List<string>();
            foreach(var pair in bindings)if(DebugPolicy.IsControllerKey((int)pair.Value))removed.Add(pair.Key);
            foreach(string key in removed)bindings.Remove(key);
            typeof(Modding.Mod).GetMethod("SaveGlobalSettings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Modding.ModHooks.GetMod("DebugMod"),null);
            DebugMod.KeyBindPanel.UpdateHelpText();return removed.Count;
        }
        public static void RestoreTime()
        {
            debug.GetField("CurrentTimeScale",Fields).SetValue(null,1f);debug.GetField("TimeScaleActive",Fields).SetValue(null,false);debug.GetField("PauseGameNoUIActive",Fields).SetValue(null,false);
            var manager=GameManager.UnsafeInstance;
            if(manager)
            {
                var scale=manager.GetComponent<DebugMod.MonoBehaviours.TimeScale>();if(scale)UnityEngine.Object.Destroy(scale);
                var cursor=manager.GetComponent<DebugMod.MonoBehaviours.MyCursor>();if(cursor)UnityEngine.Object.Destroy(cursor);
                var cameras=UnityEngine.Object.FindObjectOfType<GameCameras>();if(cameras)cameras.ResumeCameraShake();
            }
            ApplyDefaultTimeScale(1f);
        }
        public static void ApplyDefaultTimeScale(float value)
        {var manager=GameManager.UnsafeInstance;TimeController.GenericTimeScale=value;Time.timeScale=manager&&manager.IsGamePaused()?0:value;}
        public static Coroutine StartWhenActive(MonoBehaviour owner,IEnumerator routine)
        {return owner&&owner.gameObject.activeInHierarchy?owner.StartCoroutine(routine):null;}
        public static bool IsMenuScene(UnityEngine.SceneManagement.Scene scene) {return DebugPolicy.IsMenuScene(scene.name);}
        public static GameVersionData CreateVersionData() {return ScriptableObject.CreateInstance<GameVersionData>();}
    }
}
