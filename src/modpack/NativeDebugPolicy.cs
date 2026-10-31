using System;
using System.Collections.Generic;

namespace NativeModpack
{
    public static class DebugPolicy
    {
        public static bool IsControllerKey(int key) {return key>=330&&key<=509;}
        public static bool AcceptKey(int key,bool controllerShortcuts) {return key>0&&(!IsControllerKey(key)||controllerShortcuts);}
        public static int MouseButton(int key) {int index=key-323;return index<3?index+1:index+7;}
        public static Dictionary<string,int> SnapshotBindings(Dictionary<string,int> bindings)
        {return new Dictionary<string,int>(bindings);}
        public static int BindingOrNone(Dictionary<string,int> bindings,string name)
        {int value;return bindings.TryGetValue(name,out value)?value:int.MinValue;}
        public static bool IsMenuScene(string name)
        {return name=="Menu_Title"||name=="Quit_To_Menu"||name=="Pre_Menu_Intro";}
    }
    public sealed class BindingPages
    {
        public Dictionary<string,List<string>> pages=new Dictionary<string,List<string>>();
        public Dictionary<string,string> owners=new Dictionary<string,string>();
        public List<string> keys=new List<string>();
        public int selected;
        public static BindingPages Create(Dictionary<string,List<string>> input,List<string> keys,Dictionary<string,string> owners,int selected,int limit)
        {
            if(limit<=0)throw new ArgumentOutOfRangeException("limit");
            var groups=new Dictionary<string,List<string>>();var order=new List<string>();
            string selectedOwner=null;int selectedPart=0;
            for(int i=0;i<keys.Count;i++)
            {
                string key=keys[i],owner;List<string> values;
                if(!input.TryGetValue(key,out values))continue;
                if(!owners.TryGetValue(key,out owner))owner=key;
                if(i==selected){selectedOwner=owner;for(int j=0;j<i;j++){string earlier;if(!owners.TryGetValue(keys[j],out earlier))earlier=keys[j];if(earlier==owner)selectedPart++;}}
                if(!groups.ContainsKey(owner)){groups[owner]=new List<string>();order.Add(owner);}
                foreach(string value in values)if(!groups[owner].Contains(value))groups[owner].Add(value);
            }
            var result=new BindingPages();
            foreach(string owner in order)
            {
                var values=groups[owner];int count=Math.Max(1,(values.Count+limit-1)/limit);
                int start=result.keys.Count;
                for(int i=0;i<count;i++)
                {
                    string key=count==1?owner:owner+" · "+(i+1)+"/"+count;
                    int offset=i*limit,length=Math.Min(limit,values.Count-offset);
                    result.pages[key]=values.GetRange(offset,length);result.keys.Add(key);result.owners[key]=owner;
                }
                if(owner==selectedOwner)result.selected=start+Math.Min(selectedPart,count-1);
            }
            return result;
        }
    }
}
