using UnityEngine;

namespace Oathmarch.Bodies
{
    /// <summary>Material factory that never silently fails: Resources shaders first, then built-ins, loud errors.</summary>
    public static class Visuals
    {
        static Shader lit, unlit, fog;
        public static bool LitOk, UnlitOk, FogOk;

        public static void Init()
        {
            lit = Load("Shaders/SimpleLit", "Oathmarch/SimpleLit", "Standard", "Diffuse");
            unlit = Load("Shaders/UnlitColor", "Oathmarch/UnlitColor", "Unlit/Color");
            fog = Load("Shaders/FogOverlay", "Oathmarch/FogOverlay", "Unlit/Transparent", "Sprites/Default");
            LitOk = lit != null && lit.isSupported; UnlitOk = unlit != null && unlit.isSupported; FogOk = fog != null && fog.isSupported;
            if (!FogOk) Debug.LogError("[Oathmarch] FOG SHADER UNAVAILABLE: Assets/Resources/Shaders/FogOverlay.shader failed to load or is unsupported. Check the Console for shader compile errors.");
            if (!LitOk) Debug.LogError("[Oathmarch] Greybox shader unavailable (Resources/Shaders/SimpleLit). Is this project using the Built-in render pipeline?");
        }

        static Shader Load(string resource, params string[] names)
        {
            var s = Resources.Load<Shader>(resource);
            if (s != null && s.isSupported) return s;
            foreach (var n in names) { s = Shader.Find(n); if (s != null && s.isSupported) return s; }
            return null;
        }

        public static Material Lit(Color c)
        {
            if (lit == null) Init();
            var m = new Material(lit != null ? lit : Shader.Find("Hidden/InternalErrorShader")) { color = c };
            return m;
        }

        public static Material Unlit(Color c)
        {
            if (unlit == null) Init();
            return new Material(unlit != null ? unlit : lit) { color = c };
        }

        public static Material Fog(Texture tex)
        {
            if (fog == null) Init();
            if (fog == null) return null;
            var m = new Material(fog) { mainTexture = tex, renderQueue = 3010 };
            return m;
        }
    }
}
