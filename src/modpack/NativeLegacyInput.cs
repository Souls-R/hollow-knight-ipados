using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NativeModpack
{
    public static class LegacyInput
    {
        static readonly byte[] current=new byte[256],previous=new byte[256];
        static int frame=-1;
        static bool connected;
        static bool? controllerShortcuts;
        public static bool ControllerShortcuts
        {
            get {if(!controllerShortcuts.HasValue)controllerShortcuts=PlayerPrefs.GetInt("NativeDebugControllerShortcuts",0)!=0;return controllerShortcuts.Value;}
            set {controllerShortcuts=value;PlayerPrefs.SetInt("NativeDebugControllerShortcuts",value?1:0);PlayerPrefs.Save();}
        }
        public static bool HardwareKeyboardPresent {get {Read();return connected;}}
        [DllImport("__Internal")] static extern int NativeKeyboard_CopyState(IntPtr destination,int length);
        static unsafe void Read()
        {
            if(frame==Time.frameCount)return;
            Array.Copy(current,previous,current.Length);
            fixed(byte* target=current)connected=NativeKeyboard_CopyState((IntPtr)target,current.Length)!=0;
            frame=Time.frameCount;
        }
        static int Usage(int key)
        {
            if(key>=97&&key<=122)return key-97+4;
            if(key>=49&&key<=57)return key-49+30;
            if(key>=282&&key<=293)return key-282+58;
            if(key>=294&&key<=296)return key-294+104;
            if(key>=257&&key<=265)return key-257+89;
            switch(key)
            {
                case 48:return 39;case 13:return 40;case 27:return 41;case 8:return 42;case 9:return 43;
                case 32:return 44;case 45:return 45;case 61:return 46;case 91:return 47;case 93:return 48;
                case 92:return 49;case 59:return 51;case 39:return 52;case 96:return 53;case 44:return 54;
                case 46:return 55;case 47:return 56;case 301:return 57;case 316:return 70;case 302:return 71;
                case 19:return 72;case 277:return 73;case 278:return 74;case 280:return 75;case 127:return 76;
                case 279:return 77;case 281:return 78;case 275:return 79;case 276:return 80;case 274:return 81;case 273:return 82;
                case 300:return 83;case 267:return 84;case 268:return 85;case 269:return 86;case 270:return 87;
                case 271:return 88;case 256:return 98;case 266:return 99;case 272:return 103;
                case 306:return 224;case 304:return 225;case 308:return 226;case 310:case 311:return 227;
                case 305:return 228;case 303:return 229;case 307:case 313:return 230;case 309:case 312:return 231;
                default:return 0;
            }
        }
        public static bool GetKeyDown(KeyCode key)
        {
            if((int)key>=323&&(int)key<=329)
            {
                var mouse=InControl.InputManager.MouseProvider;
                if(mouse!=null&&mouse.HasMousePresent())return mouse.GetButtonWasPressed((InControl.Mouse)DebugPolicy.MouseButton((int)key));
            }
            Read();int usage=Usage((int)key);
            if(connected&&usage!=0)return current[usage]!=0&&previous[usage]==0;
            return Input.GetKeyDown(key);
        }
        public static bool DebugGetKeyDown(KeyCode key)
        {return DebugPolicy.AcceptKey((int)key,ControllerShortcuts)&&GetKeyDown(key);}
        public static Vector3 MousePosition()
        {
            var mouse=InControl.InputManager.MouseProvider;
            return mouse==null?Input.mousePosition:(Vector3)mouse.GetPosition();
        }
    }
}
