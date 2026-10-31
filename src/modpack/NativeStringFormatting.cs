using System;
using System.Globalization;
using System.Text;

namespace System.Runtime.CompilerServices
{
    // The API uses this formatting ABI; no desktop MonoMod runtime is needed.
    public struct DefaultInterpolatedStringHandler
    {
        StringBuilder builder;
        IFormatProvider provider;
        public DefaultInterpolatedStringHandler(int literals,int holes):this(literals,holes,null) { }
        public DefaultInterpolatedStringHandler(int literals,int holes,IFormatProvider formatProvider)
        {builder=new StringBuilder(Math.Max(16,literals+holes*8));provider=formatProvider;}
        public void AppendLiteral(string text) {builder.Append(text);}
        public void AppendFormatted<T>(T value) {AppendFormatted(value,0,null);}
        public void AppendFormatted<T>(T value,string format) {AppendFormatted(value,0,format);}
        public void AppendFormatted<T>(T value,int alignment) {AppendFormatted(value,alignment,null);}
        public void AppendFormatted<T>(T value,int alignment,string format)
        {
            ICustomFormatter custom=provider==null?null:provider.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
            string text=custom!=null?custom.Format(format,value,provider):
                value is IFormattable?((IFormattable)value).ToString(format,provider):
                (object)value==null?string.Empty:value.ToString();
            AppendText(text,alignment);
        }
        public void AppendFormatted(string value) {builder.Append(value);}
        public void AppendFormatted(string value,int alignment,string format) {AppendText(value,alignment);}
        public void AppendFormatted(object value,int alignment,string format) {AppendFormatted<object>(value,alignment,format);}
        void AppendText(string text,int alignment)
        {
            text=text??string.Empty;
            int pad=Math.Abs(alignment)-text.Length;
            if(alignment>0&&pad>0)builder.Append(' ',pad);
            builder.Append(text);
            if(alignment<0&&pad>0)builder.Append(' ',pad);
        }
        public override string ToString() {return builder==null?string.Empty:builder.ToString();}
        public string ToStringAndClear() {string result=ToString();Clear();return result;}
        public void Clear() {builder=null;provider=null;}
    }
}
