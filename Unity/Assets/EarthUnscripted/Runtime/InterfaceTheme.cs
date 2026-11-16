using UnityEngine;
namespace EarthUnscripted
{
    public static class InterfaceTheme
    {
        static Font font,displayFont;static Texture2D normal,hover,active,primary,panel,disc;static GUISkin skin;
        static GUIStyle card;
        public static readonly Color Navy = new Color(.025f,.053f,.08f);
        public static readonly Color Paper = new Color(.96f,.94f,.87f);
        public static readonly Color Gold = new Color(.94f,.72f,.39f);
        public static readonly Color Muted = new Color(.65f,.73f,.76f);
        public static Font DisplayFont => displayFont ? displayFont : (displayFont=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Georgia","Times New Roman","Liberation Serif"},66));
        public static Texture2D PrimaryTexture { get { Apply();return primary; } }
        public static Font Font
        {get{if(!font)font=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Helvetica Neue","Segoe UI","Arial","Liberation Sans"},24);return font;}}
        public static void Fill(Rect bounds,Color colour)
        {var previous=GUI.color;GUI.color=colour;GUI.DrawTexture(bounds,Texture2D.whiteTexture);GUI.color=previous;}
        public static void Card(Rect bounds,float opacity=1)
        {
            Apply();var previous=GUI.color;GUI.color=new Color(1,1,1,opacity);GUI.Box(bounds,GUIContent.none,card);GUI.color=previous;
        }
        public static void Disc(Rect bounds,Color colour)
        {
            if(!disc){const int n=64;var pixels=new Color[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++)pixels[y*n+x]=new Color(1,1,1,Mathf.Clamp01(32-Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32))));disc=new Texture2D(n,n,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};disc.SetPixels(pixels);disc.Apply(false,true);}
            var previous=GUI.color;GUI.color=colour;GUI.DrawTexture(bounds,disc);GUI.color=previous;
        }
        static Texture2D Tile(Color color)
        {
            color=color.linear;
            const int n=32;var pixels=new Color[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=Mathf.Max(0,7-Mathf.Min(x,n-1-x)),dy=Mathf.Max(0,7-Mathf.Min(y,n-1-y));float coverage=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(6,7,Mathf.Sqrt(dx*dx+dy*dy)));pixels[y*n+x]=new Color(color.r,color.g,color.b,color.a*coverage);}var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear};texture.SetPixels(pixels);texture.Apply();return texture;
        }
        /// <summary>Visible check box shared by all boolean menu options.</summary>
        static readonly System.Collections.Generic.Dictionary<GUIStyle,GUIStyle> checkboxLabels =
            new System.Collections.Generic.Dictionary<GUIStyle,GUIStyle>();

        // A caller may pass GUI.skin.toggle. Its background contains a native
        // checkbox icon, so use a clean text-only clone beside our own square.
        static GUIStyle CheckboxLabel(GUIStyle source)
        {
            source=source??GUI.skin.label;
            if(checkboxLabels.TryGetValue(source,out var label))return label;
            label=new GUIStyle(source){imagePosition=ImagePosition.TextOnly,fixedWidth=0,fixedHeight=0,
                padding=new RectOffset(),margin=new RectOffset(),contentOffset=Vector2.zero};
            foreach(var state in new[]{label.normal,label.hover,label.active,label.focused,
                label.onNormal,label.onHover,label.onActive,label.onFocused})
            {state.background=null;}
            checkboxLabels.Add(source,label);return label;
        }

        static Texture2D checkMark;
        // Rasterize once in checkbox-local coordinates. Rotating GUI.matrix to draw
        // strokes displaced them when the parent menu was scaled/letterboxed.
        static Texture2D CheckMark
        {
            get
            {
                if(checkMark)return checkMark;
                const int size=44;var pixels=new Color[size*size];
                var a=new Vector2(5,11);var b=new Vector2(9,15);var c=new Vector2(18,5);
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    var point=new Vector2((x+.5f)/2,22-(y+.5f)/2);
                    float distance=Mathf.Min(StrokeDistance(point,a,b),StrokeDistance(point,b,c));
                    pixels[y*size+x]=new Color(1,1,1,Mathf.Clamp01((1.6f-distance)/.6f));
                }
                checkMark=new Texture2D(size,size,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                checkMark.SetPixels(pixels);checkMark.Apply(false,true);return checkMark;
            }
        }
        static float StrokeDistance(Vector2 point,Vector2 a,Vector2 b)
        {
            var direction=b-a;
            return Vector2.Distance(point,a+direction*Mathf.Clamp01(Vector2.Dot(point-a,direction)/direction.sqrMagnitude));
        }

        public static bool CheckBox(Rect rect,bool selected,string label,GUIStyle style=null)
        {
            bool clicked=ControllerMenuGUI.CheckboxClick(rect,label);
            var box=new Rect(rect.x,rect.y+2,22,22);var previous=GUI.color;
            GUI.color=GUI.enabled?Muted:new Color(.25f,.3f,.32f);
            GUI.DrawTexture(box,Texture2D.whiteTexture);
            GUI.color=new Color(.035f,.055f,.07f);
            GUI.DrawTexture(new Rect(box.x+2,box.y+2,18,18),Texture2D.whiteTexture);
            if(selected)
            {
                GUI.color=GUI.enabled?Gold:Color.gray;
                GUI.DrawTexture(box,CheckMark);
            }
            GUI.color=previous;
            GUI.Label(new Rect(rect.x+32,rect.y,rect.width-32,rect.height),label,CheckboxLabel(style));
            return clicked?!selected:selected;
        }
        public static void Apply()
        {
            if(!skin)
            {
                skin=Object.Instantiate(GUI.skin);normal=Tile(new Color(.075f,.12f,.16f,.98f));hover=Tile(new Color(.14f,.22f,.27f));active=Tile(new Color(.27f,.30f,.27f));primary=Tile(Gold);panel=Tile(new Color(.028f,.055f,.08f,.98f));skin.font=Font;
                foreach(var style in new[]{skin.button,skin.textField,skin.textArea,skin.box})
                {
                    style.font=Font;style.normal.background=normal;style.hover.background=hover;style.active.background=active;style.focused.background=hover;style.onNormal.background=active;style.onHover.background=active;style.onActive.background=hover;style.onFocused.background=active;
                    foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})state.textColor=Paper;
                    style.border=new RectOffset(8,8,8,8);style.clipping=TextClipping.Clip;
                }
                skin.button.fontSize=16;skin.button.padding=new RectOffset(15,15,6,6);skin.textField.fontSize=17;skin.textField.padding=new RectOffset(10,10,7,7);skin.label.font=Font;skin.label.normal.textColor=Paper;
                card=new GUIStyle(skin.box);foreach(var state in new[]{card.normal,card.hover,card.active,card.focused,card.onNormal,card.onHover,card.onActive,card.onFocused})state.background=panel;
                skin.horizontalSlider.normal.background=Tile(new Color(.24f,.32f,.37f));skin.horizontalSlider.fixedHeight=5;skin.horizontalSlider.margin=new RectOffset(0,0,9,9);
                skin.horizontalSliderThumb.normal.background=primary;skin.horizontalSliderThumb.hover.background=primary;skin.horizontalSliderThumb.active.background=primary;skin.horizontalSliderThumb.fixedWidth=14;skin.horizontalSliderThumb.fixedHeight=20;
            }
            GUI.skin=skin;
        }
    }
}
