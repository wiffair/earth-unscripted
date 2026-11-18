using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Shared, renderer-independent artwork and loading presentation.</summary>
    public static class BrandScreen
    {
        static Texture2D artwork, shade;
        static GUIStyle wordmark, caption, status, eyebrow;
        public static Texture2D Artwork => artwork ? artwork : (artwork = Resources.Load<Texture2D>("EarthUnscripted/Splash"));
        public static Rect Canvas
        {
            get { float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);float width=Screen.width/scale,height=Screen.height/scale;return new Rect((1440-width)*.5f,(900-height)*.5f,width,height); }
        }
        static void Styles()
        {
            if (wordmark != null) return;
            wordmark = new GUIStyle(GUI.skin.label) { font = InterfaceTheme.DisplayFont, fontSize = 66, fontStyle = FontStyle.Bold, wordWrap = false };
            wordmark.normal.textColor = InterfaceTheme.Paper;
            caption = new GUIStyle(GUI.skin.label) { font = InterfaceTheme.Font, fontSize = 22, wordWrap = true };
            caption.normal.textColor = InterfaceTheme.Paper;
            status = new GUIStyle(caption) { fontSize = 16 };
            eyebrow = new GUIStyle(caption) { fontSize = 12, fontStyle = FontStyle.Bold };
            eyebrow.normal.textColor = InterfaceTheme.Gold;
            shade = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[128];
            for (int x = 0; x < pixels.Length; x++) pixels[x] = new Color(.018f, .035f, .055f, Mathf.Lerp(.94f, .12f, x / 127f));
            shade.SetPixels(pixels); shade.Apply(false, true);
        }
        public static void Background(Rect bounds, float dim = 0)
        {
            Styles();
            InterfaceTheme.Fill(bounds, InterfaceTheme.Navy);
            if (Artwork) GUI.DrawTexture(bounds, Artwork, ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(bounds, shade);
            if (dim > 0) InterfaceTheme.Fill(bounds, new Color(.015f, .03f, .045f, dim));
        }
        public static void Wordmark(Rect bounds, int size = 66)
        {
            Styles(); int previous = wordmark.fontSize; wordmark.fontSize = size;
            GUI.Label(bounds, ProductIdentity.Name, wordmark); wordmark.fontSize = previous;
        }
        public static void DrawLoading(string heading, string location, string message, bool failed, bool splash = false, float progress = -1)
        {
            InterfaceTheme.Apply(); Styles();
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.identity;
            InterfaceTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), InterfaceTheme.Navy);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1440 * scale) * .5f, (Screen.height - 900 * scale) * .5f), Quaternion.identity, Vector3.one * scale);
            Background(Canvas, .08f);
            GUI.Label(new Rect(74, 95, 800, 28), "A WORLD OF WAYS TO EXPLORE", eyebrow);
            Wordmark(new Rect(69, 130, 1120, 100));
            GUI.Label(new Rect(76, 235, 660, 40), ProductIdentity.Tagline, caption);
            if (!splash)
            {
                InterfaceTheme.Card(new Rect(72, 450, 740, 210));
                GUI.Label(new Rect(98, 476, 680, 32), heading, caption);
                string place=location??"";
                if(place.StartsWith("download-",System.StringComparison.Ordinal)||place.Length>90)place="Your saved location";
                GUI.Label(new Rect(98, 521, 680, 26), place, status);
                string detail=message??"Preparing the world…";
                GUI.Label(new Rect(98, 561, 680, 70), detail, status);
                if (!failed)
                {
                    // Activity, not a fictional percentage: world work has no
                    // reliable total until remote providers have responded.
                    float phase = Mathf.Repeat(Time.realtimeSinceStartup * .35f, 1);
                    InterfaceTheme.Fill(new Rect(98, 637, 680, 3), new Color(.3f, .4f, .46f, .5f));
                    if(progress>=0)InterfaceTheme.Fill(new Rect(98,637,680*Mathf.Clamp01(progress),3),InterfaceTheme.Gold);
                    else InterfaceTheme.Fill(new Rect(98 + phase * 576, 637, 104, 3), InterfaceTheme.Gold);
                }
            }
            GUI.Label(new Rect(76, 835, 1100, 24), "SINGLE PLAYER BETA   /   " + GameGraphics.Name.ToUpperInvariant() + " GRAPHICS   /   " + Application.version, eyebrow);
        }
    }

    public sealed class BrandStartupSplash : MonoBehaviour
    {
        static bool installed;
        float began;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return; installed = true;
            var root = new GameObject("Earth Unscripted startup identity");
            DontDestroyOnLoad(root); root.AddComponent<BrandStartupSplash>();
        }
        void Awake() { began = Time.realtimeSinceStartup; }
        void Update() { if (Time.realtimeSinceStartup - began >= 1.25f) Destroy(gameObject); }
        void OnGUI()
        {
            var previousMatrix = GUI.matrix; var previousColor = GUI.color; int depth = GUI.depth;
            GUI.color = Color.white; GUI.depth = -2000;
            BrandScreen.DrawLoading("", "", "", false, true);
            GUI.matrix = previousMatrix; GUI.color = previousColor; GUI.depth = depth;
        }
    }
}
