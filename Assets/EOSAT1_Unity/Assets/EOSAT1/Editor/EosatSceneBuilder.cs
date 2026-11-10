// EOSAT-1 VR - Editor menu: EOSAT-1 > Build Demo Scene.
// Creates (or reuses) materials, meshes and texture settings, builds EOS SAT-1 from
// Data/eosat1_model.json and the space scene around it, wires every component, and saves
// <EOSAT1 folder>/Scenes/EOSAT1_Demo.unity. Safe to run again: existing materials are kept
// (so your tweaks survive) unless you use "Reset Materials", but any texture they are
// missing is filled in. The EOSAT1 folder can live anywhere under Assets.
using System.Collections.Generic;
using System.Reflection;
using Eosat1.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eosat1.EditorTools
{
    public static class EosatSceneBuilder
    {
        // The EOSAT1 folder is wherever this script lives (…/EOSAT1/Editor/EosatSceneBuilder.cs),
        // so it still works if the folder ended up nested, e.g. Assets/EOSAT1_Unity/Assets/EOSAT1.
        static string rootCache;

        static string Root
        {
            get
            {
                if (string.IsNullOrEmpty(rootCache) || !AssetDatabase.IsValidFolder(rootCache)) rootCache = FindRoot();
                return rootCache;
            }
        }

        static string Generated { get { return Root + "/Generated"; } }
        static string MaterialsDir { get { return Root + "/Materials"; } }
        static string ScenesDir { get { return Root + "/Scenes"; } }
        static string ScenePath { get { return ScenesDir + "/EOSAT1_Demo.unity"; } }
        static string TexturesDir { get { return Root + "/Textures"; } }
        static string DataDir { get { return Root + "/Data"; } }

        const string ModelFile = "eosat1_model.json";

        // Things the build looked for and could not find, reported at the end.
        static readonly List<string> missing = new List<string>();

        static string FindRoot()
        {
            const string tail = "/Editor/EosatSceneBuilder.cs";
            foreach (string guid in AssetDatabase.FindAssets("EosatSceneBuilder t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (p.EndsWith(tail, System.StringComparison.OrdinalIgnoreCase)) return p.Substring(0, p.Length - tail.Length);
            }
            return "Assets/EOSAT1";
        }

        /// <summary>
        /// Load an asset from the expected place, or find it by file name anywhere in the
        /// project. Records it as missing when it can't be found.
        /// </summary>
        static T FindAsset<T>(string folder, string fileName, string typeFilter) where T : Object
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(folder + "/" + fileName);
            if (a != null) return a;
            string stem = fileName;
            int dot = stem.IndexOf('.');
            if (dot > 0) stem = stem.Substring(0, dot);
            foreach (string guid in AssetDatabase.FindAssets(stem + " t:" + typeFilter))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(p).Equals(fileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    a = AssetDatabase.LoadAssetAtPath<T>(p);
                    if (a != null) return a;
                }
            }
            if (!missing.Contains(fileName)) missing.Add(fileName);
            return null;
        }

        [MenuItem("EOSAT-1/Build Demo Scene", false, 0)]
        public static void BuildDemoScene()
        {
            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("EOSAT-1", "Rebuild " + ScenePath + "?\nChanges made inside that scene will be replaced. " +
                                                        "Materials you edited are kept.", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        [MenuItem("EOSAT-1/Open Demo Scene", false, 1)]
        public static void OpenDemoScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("EOSAT-1", "No scene yet. Use EOSAT-1 > Build Demo Scene first.", "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("EOSAT-1/Reset Materials and Rebuild", false, 20)]
        public static void ResetMaterialsAndRebuild()
        {
            if (!EditorUtility.DisplayDialog("EOSAT-1", "Reset every EOSAT-1 material to its default settings and rebuild the scene?",
                    "Reset", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
        }

        static void Build(bool resetMaterials)
        {
            missing.Clear();
            rootCache = null;

            // the spacecraft comes from the parts list; without it there is nothing to build
            var modelAsset = FindAsset<TextAsset>(DataDir, ModelFile, "TextAsset");
            Dictionary<string, object> model = null;
            object parsed;
            string error = "file not found";
            if (modelAsset != null && MiniJson.TryParse(modelAsset.text, out parsed, out error)) model = parsed as Dictionary<string, object>;
            if (model == null)
            {
                EditorUtility.DisplayDialog("EOSAT-1", "Could not read the spacecraft model " + DataDir + "/" + ModelFile + " (" + error +
                    ").\n\nCopy the Data folder from the zip next to Scripts in " + Root + " and try again.", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("EOSAT-1", "Folders and textures", 0.1f);
                EnsureFolder(Generated);
                EnsureFolder(MaterialsDir);
                EnsureFolder(ScenesDir);
                ConfigureTextures();

                EditorUtility.DisplayProgressBar("EOSAT-1", "Materials", 0.25f);
                var mats = new Mats();
                mats.Create(resetMaterials, MiniJson.Obj(model, "materials"));

                EditorUtility.DisplayProgressBar("EOSAT-1", "Meshes (globe, stars, spacecraft)", 0.4f);
                var meshes = new MeshSet();
                meshes.Create();

                EditorUtility.DisplayProgressBar("EOSAT-1", "Scene", 0.7f);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject satellite = BuildScene(mats, meshes, model);
                ConfigureProject();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddToBuildSettings(ScenePath);
                AssetDatabase.SaveAssets();

                Selection.activeGameObject = satellite;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (missing.Count > 0)
            {
                string list = string.Join(", ", missing.ToArray());
                Debug.LogWarning("EOSAT-1: built the scene, but could not find: " + list +
                                 ". Expected them under " + TexturesDir + " and " + DataDir + ".");
                EditorUtility.DisplayDialog("EOSAT-1 - some files are missing",
                    "The scene was built in " + Root + ", but these files were not found anywhere in the project:\n\n" + list +
                    "\n\nWithout them parts of the scene show up plain white, and there may be no stars. " +
                    "Copy the Textures and Data folders from the zip next to Scripts in " + Root +
                    ", then use EOSAT-1 > Reset Materials and Rebuild.", "OK");
                return;
            }
            EditorUtility.DisplayDialog("EOSAT-1",
                "Scene built: " + ScenePath + "\nAll textures and data found.\n\nPress Play. Drag with the mouse to look around, " +
                "wheel to zoom, V for the next view (the first one matches the reference picture), 1/2 fold out the solar panels, " +
                "3 the antenna, 4 jams it, = speeds time up. Full list at the bottom of the Game view.\n\n" +
                "Headset: see README (XR Plug-in Management + OpenXR).", "OK");
        }

        // ------------------------------------------------------------------ assets
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void ConfigureTextures()
        {
            Import("earth_day.png", 4096, TextureWrapMode.Repeat, TextureWrapMode.Clamp, true, false);
            Import("earth_night.png", 2048, TextureWrapMode.Repeat, TextureWrapMode.Clamp, true, false);
            Import("moon.png", 1024, TextureWrapMode.Repeat, TextureWrapMode.Clamp, true, false);
            // EOS SAT-1: whole-panel pictures, their metal/smoothness masks (linear), and the gold foil
            Import("panel_a.png", 2048, TextureWrapMode.Clamp, TextureWrapMode.Clamp, true, false);
            Import("panel_b.png", 2048, TextureWrapMode.Clamp, TextureWrapMode.Clamp, true, false);
            Import("panel_a_mask.png", 2048, TextureWrapMode.Clamp, TextureWrapMode.Clamp, false, false);
            Import("panel_b_mask.png", 2048, TextureWrapMode.Clamp, TextureWrapMode.Clamp, false, false);
            Import("mli_gold.png", 1024, TextureWrapMode.Repeat, TextureWrapMode.Repeat, true, false);
            Import("mli_gold_normal.png", 1024, TextureWrapMode.Repeat, TextureWrapMode.Repeat, false, true);
        }

        static void Import(string file, int maxSize, TextureWrapMode u, TextureWrapMode v, bool srgb, bool normalMap)
        {
            Texture2D tex = FindAsset<Texture2D>(TexturesDir, file, "Texture2D");
            if (tex == null) return;
            var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
            if (ti == null) return;
            bool changed = false;
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type) { ti.textureType = type; changed = true; }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; changed = true; }
            if (ti.wrapModeU != u) { ti.wrapModeU = u; changed = true; }
            if (ti.wrapModeV != v) { ti.wrapModeV = v; changed = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (ti.anisoLevel < 8) { ti.anisoLevel = 8; changed = true; }
            if (!normalMap && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                changed = true;
            }
            if (changed) ti.SaveAndReimport();
        }

        static Texture2D Tex(string file)
        {
            return FindAsset<Texture2D>(TexturesDir, file, "Texture2D");
        }

        static Mesh SaveMesh(Mesh m, string file)
        {
            string path = Generated + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(m, existing);
                existing.name = m.name;
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // mesh asset names already used in this build, so two parts never overwrite each other's mesh
        static readonly HashSet<string> meshFiles = new HashSet<string>();

        static string MeshFile(string partName)
        {
            var sb = new System.Text.StringBuilder("Sat_", partName.Length + 12);
            foreach (char c in partName)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '+') sb.Append('p');
                else if (c == '-') sb.Append('m');
                else sb.Append('_');
            }
            string stem = sb.ToString(), file = stem + ".asset";
            for (int i = 2; meshFiles.Contains(file); i++) file = stem + "_" + i + ".asset";
            meshFiles.Add(file);
            return file;
        }

        sealed class MeshSet
        {
            public Mesh Globe, LowGlobe, Stars, Cone, Ring;

            public void Create()
            {
                Globe = SaveMesh(ProceduralMeshes.UvSphere(192, 96, "EOSAT1 Globe"), "Globe.asset");
                LowGlobe = SaveMesh(ProceduralMeshes.UvSphere(64, 32, "EOSAT1 Globe Low"), "GlobeLow.asset");
                var catalog = FindAsset<TextAsset>(DataDir, "bright_stars.txt", "TextAsset");
                Stars = SaveMesh(ProceduralMeshes.StarField(catalog != null ? catalog.text : "", "EOSAT1 Stars"), "Stars.asset");
                Cone = SaveMesh(ProceduralMeshes.Cone(20, "EOSAT1 Cone"), "Cone.asset");
                Ring = SaveMesh(ProceduralMeshes.Ring(64, 0.72f, "EOSAT1 Ring"), "Ring.asset");
            }
        }

        sealed class Mats
        {
            public Material Hands;
            public Material AxisX, AxisY, AxisZ, Boresight, Footprint, JamGlow;
            public Material Earth, Atmosphere, Moon, Sun, Stars, Floor, Hud, HudBack;
            readonly Dictionary<string, Material> model = new Dictionary<string, Material>();
            Dictionary<string, object> modelDefs;
            bool reset;

            public void Create(bool resetAll, Dictionary<string, object> spacecraftMaterials)
            {
                reset = resetAll;
                modelDefs = spacecraftMaterials ?? new Dictionary<string, object>();
                Hands = Lit("EOSAT1 Controllers", new Color(0.3f, 0.32f, 0.36f), 0.2f, 0.5f, null);

                AxisX = Custom("EOSAT1 Axis X", "EOSAT1/UnlitColor", m => m.SetColor("_Color", new Color(0.95f, 0.25f, 0.2f)));
                AxisY = Custom("EOSAT1 Axis Y", "EOSAT1/UnlitColor", m => m.SetColor("_Color", new Color(0.3f, 0.9f, 0.35f)));
                AxisZ = Custom("EOSAT1 Axis Z", "EOSAT1/UnlitColor", m => m.SetColor("_Color", new Color(0.3f, 0.55f, 1f)));
                Boresight = Custom("EOSAT1 Boresight", "EOSAT1/UnlitColor", m => m.SetColor("_Color", new Color(1f, 0.85f, 0.2f)));
                Footprint = Custom("EOSAT1 Footprint", "EOSAT1/UnlitColor", m => m.SetColor("_Color", new Color(1f, 0.85f, 0.2f)));
                JamGlow = Custom("EOSAT1 Jam Glow", "EOSAT1/GlowAdditive", m => m.SetColor("_Color", new Color(1f, 0.12f, 0.08f)));
                Earth = Custom("EOSAT1 Earth", "EOSAT1/Planet", m =>
                {
                    m.SetTexture("_DayTex", Tex("earth_day.png"));
                    m.SetTexture("_NightTex", Tex("earth_night.png"));
                }, "_DayTex", "_NightTex");
                Atmosphere = Custom("EOSAT1 Atmosphere", "EOSAT1/Atmosphere", m => { });
                Moon = Custom("EOSAT1 Moon", "EOSAT1/Planet", m =>
                {
                    m.SetTexture("_DayTex", Tex("moon.png"));
                    m.SetFloat("_NightIntensity", 0f);
                    m.SetFloat("_GlintStrength", 0f);
                    m.SetFloat("_RimStrength", 0f);
                    m.SetFloat("_Exposure", 1.0f);
                    m.SetFloat("_DarkSide", 0.012f);
                }, "_DayTex");
                Sun = Custom("EOSAT1 Sun", "EOSAT1/SunGlow", m => m.SetFloat("_DiscRadius", 1f / 24f));
                Stars = Custom("EOSAT1 Stars", "EOSAT1/Stars", m => { });
                Floor = Custom("EOSAT1 Comfort Floor", "EOSAT1/GridFloor", m => { });
                if (Floor != null)
                {
                    Floor.SetFloat("_Radius", 4.0f);     // EOS SAT-1 is ~1.4 m (1.8 m with the panels out)
                    EditorUtility.SetDirty(Floor);
                }
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Hud = Custom("EOSAT1 Text", "EOSAT1/HudText", m =>
                {
                    if (font != null && font.material != null) m.SetTexture("_MainTex", font.material.mainTexture);
                }, "_MainTex");
                HudBack = Custom("EOSAT1 Text Panel", "EOSAT1/HudText", m => m.SetColor("_Color", new Color(0.02f, 0.03f, 0.05f, 0.78f)));
            }

            /// <summary>A spacecraft material, defined in eosat1_model.json ("materials").</summary>
            public Material Model(string key)
            {
                if (string.IsNullOrEmpty(key)) key = "dark";
                Material m;
                if (model.TryGetValue(key, out m)) return m;
                var def = MiniJson.Obj(modelDefs, key) ?? new Dictionary<string, object>();
                m = ModelMaterial("EOSAT1 Sat " + key, def);
                model[key] = m;
                return m;
            }

            static Shader LitShader()
            {
                RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
                if (rp != null)
                {
                    string n = rp.GetType().Name;
                    if (n.Contains("Universal"))
                    {
                        Shader s = Shader.Find("Universal Render Pipeline/Lit");
                        if (s != null) return s;
                    }
                    if (n.Contains("HD"))
                    {
                        Shader s = Shader.Find("HDRP/Lit");
                        if (s != null) return s;
                    }
                }
                return Shader.Find("Standard");
            }

            static Texture MainTexture(Material m)
            {
                if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
                if (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) return m.GetTexture("_BaseColorMap");
                if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
                return null;
            }

            static void SetMainTexture(Material m, Texture tex)
            {
                if (tex == null) return;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
            }

            Material Lit(string name, Color color, float metallic, float smoothness, Texture tex)
            {
                string path = MaterialsDir + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader shader = LitShader();
                if (existing != null && !reset && existing.shader == shader)
                {
                    // keep the user's tweaks, but fill in a texture that was missing when it was made
                    if (tex != null && MainTexture(existing) == null)
                    {
                        SetMainTexture(existing, tex);
                        EditorUtility.SetDirty(existing);
                    }
                    return existing;
                }
                Material m = existing != null ? existing : new Material(shader);
                m.shader = shader;
                m.name = name;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
                SetMainTexture(m, tex);
                if (existing == null) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                return m;
            }

            /// <summary>Lit material from a model definition: colour, metallic, smoothness, and optional
            /// colour texture, normal map and metal/smoothness mask (R = metallic, A = smoothness).</summary>
            Material ModelMaterial(string name, Dictionary<string, object> def)
            {
                Color color = ColorOf(def, "color", new Color(0.8f, 0.8f, 0.8f));
                float metallic = (float)Num(def, "metallic", 0.0);
                float smoothness = (float)Num(def, "smoothness", 0.5);
                string s;
                Texture tex = MiniJson.TryString(def, "texture", out s) ? Tex(s) : null;
                Texture normal = MiniJson.TryString(def, "normal", out s) ? Tex(s) : null;
                Texture mask = MiniJson.TryString(def, "mask", out s) ? Tex(s) : null;
                float bump = (float)Num(def, "normal_scale", 1.0);

                string path = MaterialsDir + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader shader = LitShader();
                if (existing != null && !reset && existing.shader == shader)
                {
                    bool complete = (tex == null || MainTexture(existing) != null) &&
                                    (normal == null || !existing.HasProperty("_BumpMap") || existing.GetTexture("_BumpMap") != null) &&
                                    (mask == null || !existing.HasProperty("_MetallicGlossMap") || existing.GetTexture("_MetallicGlossMap") != null);
                    if (complete) return existing;
                }
                Material m = existing != null ? existing : new Material(shader);
                m.shader = shader;
                m.name = name;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
                SetMainTexture(m, tex);
                if (normal != null && m.HasProperty("_BumpMap"))
                {
                    m.SetTexture("_BumpMap", normal);
                    if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", bump);
                    m.EnableKeyword("_NORMALMAP");
                }
                if (mask != null && m.HasProperty("_MetallicGlossMap"))
                {
                    m.SetTexture("_MetallicGlossMap", mask);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");      // URP Lit
                    m.EnableKeyword("_METALLICGLOSSMAP");          // built-in Standard
                    if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);   // alpha of the mask
                    if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", smoothness);
                }
                if (existing == null) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                return m;
            }

            Material Custom(string name, string shaderName, System.Action<Material> setup, params string[] requiredTextures)
            {
                string path = MaterialsDir + "/" + name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader shader = Shader.Find(shaderName);
                if (existing != null && !reset && shader != null && existing.shader == shader)
                {
                    // reuse it unless a texture it needs is missing (e.g. it was made before the
                    // textures were found); then set it up again
                    bool complete = true;
                    foreach (string prop in requiredTextures)
                        if (existing.HasProperty(prop) && existing.GetTexture(prop) == null) complete = false;
                    if (complete) return existing;
                }
                if (shader == null)
                {
                    Debug.LogError("EOSAT-1: shader '" + shaderName + "' not found. Is the Shaders folder inside " + Root + "?");
                    if (!missing.Contains("shader " + shaderName)) missing.Add("shader " + shaderName);
                    shader = Shader.Find("Hidden/InternalErrorShader");
                }
                Material m = existing != null ? existing : new Material(shader);
                m.shader = shader;
                m.name = name;
                setup(m);
                if (existing == null) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                return m;
            }
        }

        // ------------------------------------------------------------------ reading the parts list
        static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            double v;
            return MiniJson.TryNumber(d, key, out v) ? v : fallback;
        }

        static Color ColorOf(Dictionary<string, object> d, string key, Color fallback)
        {
            var c = new double[3];
            return MiniJson.TryNumbers(d, key, 3, c) ? new Color((float)c[0], (float)c[1], (float)c[2]) : fallback;
        }

        /// <summary>A body-frame vector in millimetres.</summary>
        static Vec3d VecMm(Dictionary<string, object> d, string key, Vec3d fallback)
        {
            var c = new double[3];
            return MiniJson.TryNumbers(d, key, 3, c) ? new Vec3d(c[0], c[1], c[2]) : fallback;
        }

        /// <summary>Body-frame millimetres to the spacecraft's local Unity metres.</summary>
        static Vector3 U(Vec3d mm)
        {
            return SimToUnity.Vec(mm * 0.001);
        }

        /// <summary>A body-frame direction as a Unity direction.</summary>
        static Vector3 UDir(Vec3d d)
        {
            return SimToUnity.Vec(d.Normalized);
        }

        static List<object> List(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as List<object>;
            return null;
        }

        static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            string s;
            return MiniJson.TryString(d, key, out s) ? s : fallback;
        }

        // ------------------------------------------------------------------ scene
        static GameObject BuildScene(Mats m, MeshSet ms, Dictionary<string, object> model)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.045f, 0.05f, 0.06f);

            // app
            var appGo = new GameObject("EOSAT-1 App");
            var app = appGo.AddComponent<EosatApp>();
            var info = appGo.AddComponent<InfoPanel>();
            var desktop = appGo.AddComponent<DesktopControls>();
            app.recording = FindAsset<TextAsset>(DataDir, "eosat1_one_orbit.jsonl.txt", "TextAsset");
            info.app = app;

            // space: EOS SAT-1 floats at about chest height, life size
            var spaceGo = new GameObject("Space");
            spaceGo.transform.position = new Vector3(0f, 1.45f, 0f);
            var space = spaceGo.AddComponent<SpaceScene>();
            Transform st = spaceGo.transform;

            SatelliteView view;
            GameObject sat = BuildSatellite(st, m, ms, model, out view);

            var earth = MeshObject("Earth", st, ms.Globe, m.Earth);
            var atmo = MeshObject("Atmosphere", earth.transform, ms.Globe, m.Atmosphere);
            atmo.transform.localScale = Vector3.one * 1.025f;
            var moon = MeshObject("Moon", st, ms.LowGlobe, m.Moon);
            var stars = MeshObject("Stars", st, ms.Stars, m.Stars);
            var sunDisc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sunDisc.name = "Sun Disc";
            Object.DestroyImmediate(sunDisc.GetComponent<Collider>());
            sunDisc.transform.SetParent(st, false);
            Plain(sunDisc.GetComponent<MeshRenderer>(), m.Sun);
            var footprint = MeshObject("Camera Footprint", st, ms.Ring, m.Footprint);

            var sunLightGo = new GameObject("Sun Light");
            sunLightGo.transform.SetParent(st, false);
            var sunLight = sunLightGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1f, 0.98f, 0.95f);
            sunLight.intensity = 1.5f;
            sunLight.shadows = LightShadows.Soft;
            sunLight.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.sun = sunLight;

            var shineGo = new GameObject("Earthshine Light");
            shineGo.transform.SetParent(st, false);
            var shine = shineGo.AddComponent<Light>();
            shine.type = LightType.Directional;
            shine.color = new Color(0.55f, 0.68f, 0.95f);
            shine.intensity = 0.3f;
            shine.shadows = LightShadows.None;
            shine.lightmapBakeType = LightmapBakeType.Realtime;

            // player rig: behind and to the left of the spacecraft (which flies along +Z),
            // turned to face it; the folded panels are on the side facing you
            var rigGo = new GameObject("Player Rig");
            rigGo.transform.position = new Vector3(-1.4f, 0f, -2.6f);
            rigGo.transform.rotation = Quaternion.Euler(0f, 28f, 0f);
            var rig = rigGo.AddComponent<VrRig>();
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(rigGo.transform, false);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset, false);
            camGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 3500f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            Transform left = Controller("Left Controller", offset, m.Hands);
            Transform right = Controller("Right Controller", offset, m.Hands);

            // info panel on the left controller (headset only)
            var panel = new GameObject("Info Panel");
            panel.transform.SetParent(left, false);
            panel.transform.localPosition = new Vector3(0f, 0.09f, 0.06f);
            panel.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Backing";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(panel.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            back.transform.localScale = new Vector3(0.34f, 0.14f, 1f);
            Plain(back.GetComponent<MeshRenderer>(), m.HudBack);
            // TextMesh line height is about fontSize x characterSize / 10 (here ~9 mm)
            TextMesh panelText = Text("Text", panel.transform, new Vector3(-0.162f, 0.062f, 0f), "", 0.0018f, Color.white, m.Hud,
                                      TextAnchor.UpperLeft, false);
            panel.SetActive(false);

            // comfort floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Comfort Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = Vector3.zero;
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(9f, 9f, 1f);
            Plain(floor.GetComponent<MeshRenderer>(), m.Floor);

            // wiring
            space.satellite = sat.transform;
            space.earth = earth.transform;
            space.moon = moon.transform;
            space.stars = stars.transform;
            space.sunDisc = sunDisc.transform;
            space.sunLight = sunLight;
            space.earthshineLight = shine;
            space.footprint = footprint.transform;
            space.cameras = new[] { cam };
            app.space = space;
            app.satelliteView = view;
            app.infoPanel = info;
            info.worldText = panelText;
            info.worldPanel = panel;
            desktop.app = app;
            desktop.infoPanel = info;
            desktop.cameraTransform = camGo.transform;
            desktop.target = sat.transform;
            rig.app = app;
            rig.infoPanel = info;
            rig.cameraOffset = offset;
            rig.head = camGo.transform;
            rig.leftHand = left;
            rig.rightHand = right;

            // pose everything once so the scene looks right before Play: sandbox at t = 1250 s,
            // camera on Earth, antenna out, panels still folded (like the reference picture)
            SandboxSim preview = SandboxSim.SlideScenario();
            var frame = new StateFrame();
            preview.Sample(1250.0, frame);
            var ds = new DisplayState();
            ds.CopyFrom(frame);
            space.Apply(ds, 0f);
            view.Apply(ds, 0f);
            space.FootprintEnabled = true;
            space.Apply(ds, 0f);
            return sat;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Plain(go.AddComponent<MeshRenderer>(), mat);
            return go;
        }

        static void Plain(MeshRenderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Transform Controller(string name, Transform parent, Material mat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Grip";
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            grip.transform.SetParent(root, false);
            grip.transform.localPosition = new Vector3(0f, -0.02f, -0.03f);
            grip.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.035f, 0.035f, 0.11f);
            grip.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(root, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0.03f);
            ring.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            ring.transform.localScale = new Vector3(0.07f, 0.006f, 0.07f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = mat;
            root.gameObject.SetActive(false);
            return root;
        }

        static TextMesh Text(string name, Transform parent, Vector3 localPos, string text, float size, Color color, Material mat,
                             TextAnchor anchor, bool faceCamera)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.richText = true;
            tm.color = color;
            Plain(go.GetComponent<MeshRenderer>(), mat);
            if (faceCamera) go.AddComponent<FaceCamera>();
            return tm;
        }

        static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // ------------------------------------------------------------------ EOS SAT-1, from the parts list
        static GameObject BuildSatellite(Transform parent, Mats m, MeshSet ms, Dictionary<string, object> model, out SatelliteView view)
        {
            var root = Group("EOS SAT-1", parent);
            Transform t = root.transform;
            view = root.AddComponent<SatelliteView>();
            meshFiles.Clear();

            // groups keep the hierarchy readable
            var groups = new Dictionary<string, Transform>();
            System.Func<string, Transform> group = gname =>
            {
                Transform g;
                if (!groups.TryGetValue(gname, out g))
                {
                    g = Group(gname, t).transform;
                    groups[gname] = g;
                }
                return g;
            };

            // joints (hinges), each a pivot object with a DeployableJoint
            var joints = new Dictionary<string, Transform>();
            var jointRole = new Dictionary<string, string>();
            var wingMy = new List<DeployableJoint>();
            var wingPy = new List<DeployableJoint>();
            var antenna = new List<DeployableJoint>();
            DeployableJoint jam = null;
            List<object> jlist = List(model, "joints");
            if (jlist != null)
            {
                foreach (object o in jlist)
                {
                    var j = o as Dictionary<string, object>;
                    if (j == null) continue;
                    string name = Str(j, "name", "joint");
                    string role = Str(j, "role", "");
                    Transform holder = role.StartsWith("wing") ? group("Solar panels") : group("UHF antenna (course)");
                    var pivot = Group("Hinge " + name, holder);
                    pivot.transform.localPosition = U(VecMm(j, "pivot", Vec3d.Zero));
                    var dj = pivot.AddComponent<DeployableJoint>();
                    dj.localAxis = UDir(VecMm(j, "axis", Vec3d.UnitZ));
                    // a body-frame rotation of +a about an axis is -a about the converted axis in Unity
                    dj.deployedAngle = -(float)Num(j, "angle", 0.0);
                    dj.CaptureStowedPose();
                    joints[name] = pivot.transform;
                    jointRole[name] = role;
                    if (role == "wing_my") wingMy.Add(dj);
                    else if (role == "wing_py") wingPy.Add(dj);
                    else if (role == "antenna") antenna.Add(dj);
                    else if (role == "antenna_jam") jam = dj;
                }
            }

            // parts
            var meshCache = new Dictionary<string, Mesh>();
            List<object> plist = List(model, "parts");
            if (plist != null)
            {
                foreach (object o in plist)
                {
                    var p = o as Dictionary<string, object>;
                    if (p == null) continue;
                    string name = Str(p, "name", "Part");
                    string type = Str(p, "type", "");
                    GameObject go = BuildPart(p, type, name, m, meshCache);
                    if (go == null) continue;
                    string jname;
                    Transform jointT = null;
                    if (MiniJson.TryString(p, "joint", out jname)) joints.TryGetValue(jname, out jointT);
                    // the pose was set in the spacecraft's axes: put the part there first, then hang
                    // it on its hinge, keeping where it is, so it turns with the hinge
                    go.transform.SetParent(group(GroupOf(name, type)), false);
                    if (jointT != null) go.transform.SetParent(jointT, true);
                    if (type == "panel" && jointT != null)
                    {
                        string role;
                        jointRole.TryGetValue(jname, out role);
                        if (role == "wing_my") view.cellsMinusY = go.transform;
                        else if (role == "wing_py") view.cellsPlusY = go.transform;
                    }
                }
            }

            // markers: jam warning, overlays, which way the deployed cells face
            var markers = MiniJson.Obj(model, "markers") ?? new Dictionary<string, object>();
            var cellN = new double[3];
            if (MiniJson.TryNumbers(markers, "deployed_cell_normal", 3, cellN))
                view.deployedCellNormalBody = new Vector3((float)cellN[0], (float)cellN[1], (float)cellN[2]);

            var haloDef = MiniJson.Obj(markers, "jam_halo") ?? new Dictionary<string, object>();
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "Jam Warning Halo";
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(group("UHF antenna (course)"), false);
            halo.transform.localPosition = U(VecMm(haloDef, "center", new Vec3d(330, -60, -530)));
            halo.transform.localScale = Vector3.one * (float)(2.0 * Num(haloDef, "radius", 180.0) * 0.001);
            Plain(halo.GetComponent<MeshRenderer>(), m.JamGlow);
            halo.GetComponent<MeshRenderer>().enabled = false;
            TextMesh jamLabel = Text("Jam Label", t, U(VecMm(markers, "jam_label", new Vec3d(330, 0, -830))), "ANTENNA JAMMED", 0.012f,
                                     new Color(1f, 0.35f, 0.3f), m.Hud, TextAnchor.MiddleCenter, true);
            jamLabel.gameObject.SetActive(false);

            double axesLen = Num(markers, "axes_length", 1150.0) * 0.001;
            GameObject axes = BuildAxes(t, ms, m, (float)axesLen);
            GameObject bore = Group("Camera Boresight", t);
            Vec3d o0 = VecMm(markers, "boresight_origin", new Vec3d(-70, -20, 515));
            double bl = Num(markers, "boresight_length", 3000.0);
            Cylinder("Ray", bore.transform, U(o0 + new Vec3d(0, 0, bl / 2)), UDir(Vec3d.UnitZ), 0.008f, (float)(bl * 0.001), m.Boresight, true);

            view.wingMinusY = wingMy.ToArray();
            view.wingPlusY = wingPy.ToArray();
            view.antennaElements = antenna.ToArray();
            view.antennaJamElement = jam;
            view.jamHalo = halo.GetComponent<MeshRenderer>();
            view.jamLabel = jamLabel.gameObject;
            view.bodyAxes = axes;
            view.boresight = bore;
            if (wingMy.Count == 0 || wingPy.Count == 0)
                Debug.LogWarning("EOSAT-1: the model has no wing_my / wing_py joints; the solar panels won't move.");
            return root;
        }

        static string GroupOf(string name, string type)
        {
            if (type == "prism") return "Bus";
            if (name.StartsWith("DragonEye") || name.StartsWith("Nadir") || name.StartsWith("Magnetometer")) return "Camera face (+Z)";
            if (name.StartsWith("Fine sun")) return "Fine sun sensors";
            if (name.StartsWith("Separation") || name.StartsWith("Star camera")) return "Rear face (-Z)";
            if (name.StartsWith("Panel hinge") || type == "panel") return "Solar panels";
            if (name.StartsWith("UHF")) return "UHF antenna (course)";
            return "Other";
        }

        static GameObject BuildPart(Dictionary<string, object> p, string type, string name, Mats m, Dictionary<string, Mesh> cache)
        {
            switch (type)
            {
                case "prism": return PrismPart(p, name, m, cache);
                case "lathe": return LathePart(p, name, m, cache);
                case "panel": return PanelPart(p, name, m, cache);
                case "box":
                {
                    Vec3d c = VecMm(p, "center", Vec3d.Zero);
                    Vec3d size = VecMm(p, "size", new Vec3d(100, 100, 100));
                    Vec3d x = VecMm(p, "x_axis", Vec3d.UnitX).Normalized;
                    Vec3d z = VecMm(p, "z_axis", Vec3d.UnitZ).Normalized;
                    x = Vec3d.RejectFrom(x, z).Normalized;
                    Vec3d y = Vec3d.Cross(z, x);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.localPosition = U(c);
                    // local x -> body x, local z -> body z (Unity's y then points along -body y; a box doesn't mind)
                    go.transform.localRotation = Quaternion.LookRotation(UDir(z), -UDir(y));
                    go.transform.localScale = new Vector3((float)(size.x * 0.001), (float)(size.y * 0.001), (float)(size.z * 0.001));
                    go.GetComponent<MeshRenderer>().sharedMaterial = m.Model(Str(p, "material", "dark"));
                    return go;
                }
                case "cylinder":
                {
                    Vec3d a = VecMm(p, "start", Vec3d.Zero), b = VecMm(p, "end", new Vec3d(0, 0, 100));
                    return Cylinder(name, null, U((a + b) * 0.5), UDir(b - a), (float)(2.0 * Num(p, "radius", 10.0) * 0.001),
                                    (float)((b - a).Length * 0.001), m.Model(Str(p, "material", "dark")), false);
                }
                case "sphere":
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.localPosition = U(VecMm(p, "center", Vec3d.Zero));
                    go.transform.localScale = Vector3.one * (float)(2.0 * Num(p, "radius", 10.0) * 0.001);
                    go.GetComponent<MeshRenderer>().sharedMaterial = m.Model(Str(p, "material", "white"));
                    return go;
                }
                default:
                    Debug.LogWarning("EOSAT-1: model part '" + name + "' has unknown type '" + type + "'; skipped.");
                    return null;
            }
        }

        static GameObject Cylinder(string name, Transform parent, Vector3 centre, Vector3 axis, float diameter, float length, Material mat, bool plain)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            var r = go.GetComponent<MeshRenderer>();
            if (plain) Plain(r, mat);
            else r.sharedMaterial = mat;
            return go;
        }

        static GameObject MeshPart(string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            return go;
        }

        static GameObject PrismPart(Dictionary<string, object> p, string name, Mats m, Dictionary<string, Mesh> cache)
        {
            var poly = new List<Vector2>();
            List<object> pl = List(p, "polygon");
            if (pl != null)
            {
                foreach (object o in pl)
                {
                    var xy = o as List<object>;
                    if (xy != null && xy.Count >= 2 && xy[0] is double && xy[1] is double)
                        poly.Add(new Vector2((float)((double)xy[0] * 0.001), (float)((double)xy[1] * 0.001)));
                }
            }
            if (poly.Count < 3) return null;
            var z = new double[2];
            if (!MiniJson.TryNumbers(p, "z", 2, z)) z = new[] { -100.0, 100.0 };
            var mats = MiniJson.Obj(p, "materials") ?? new Dictionary<string, object>();
            Mesh mesh = ProceduralMeshes.Prism(poly, (float)(z[0] * 0.001), (float)(z[1] * 0.001),
                                               (float)(Num(p, "uv_mm", 400.0) * 0.001), "EOS SAT-1 " + name);
            mesh = SaveMesh(mesh, MeshFile(name));
            return MeshPart(name, mesh, new[] { m.Model(Str(mats, "sides", "mli_gold")), m.Model(Str(mats, "front", "mli_gold")),
                                                m.Model(Str(mats, "back", "mli_gold")) });
        }

        static GameObject LathePart(Dictionary<string, object> p, string name, Mats m, Dictionary<string, Mesh> cache)
        {
            var profile = new List<Vector2>();
            List<object> pl = List(p, "profile");
            if (pl != null)
            {
                foreach (object o in pl)
                {
                    var rh = o as List<object>;
                    if (rh != null && rh.Count >= 2 && rh[0] is double && rh[1] is double)
                        profile.Add(new Vector2((float)((double)rh[0] * 0.001), (float)((double)rh[1] * 0.001)));
                }
            }
            if (profile.Count < 2) return null;
            // one submesh per distinct material, in order of first use
            var keys = new List<string>();
            var segSub = new List<int>();
            List<object> ml = List(p, "materials");
            for (int k = 0; k + 1 < profile.Count; k++)
            {
                string key = (ml != null && k < ml.Count && ml[k] is string) ? (string)ml[k] : (keys.Count > 0 ? keys[keys.Count - 1] : "dark");
                int idx = keys.IndexOf(key);
                if (idx < 0)
                {
                    keys.Add(key);
                    idx = keys.Count - 1;
                }
                segSub.Add(idx);
            }
            int segments = (int)Num(p, "segments", 48);
            // identical profiles share one mesh asset (the two DragonEye hoods, for example)
            var sig = new System.Text.StringBuilder();
            foreach (Vector2 v in profile) sig.Append(v.x.ToString("R")).Append(',').Append(v.y.ToString("R")).Append(';');
            foreach (int s in segSub) sig.Append(s).Append('|');
            sig.Append(segments);
            Mesh mesh;
            string key2 = sig.ToString();
            if (!cache.TryGetValue(key2, out mesh))
            {
                mesh = SaveMesh(ProceduralMeshes.Lathe(profile, segments, segSub, keys.Count, "EOS SAT-1 " + name), MeshFile(name));
                cache[key2] = mesh;
            }
            var mats = new Material[keys.Count];
            for (int i = 0; i < keys.Count; i++) mats[i] = m.Model(keys[i]);
            var go = MeshPart(name, mesh, mats);
            go.transform.localPosition = U(VecMm(p, "origin", Vec3d.Zero));
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, UDir(VecMm(p, "axis", Vec3d.UnitZ)));
            return go;
        }

        static GameObject PanelPart(Dictionary<string, object> p, string name, Mats m, Dictionary<string, Mesh> cache)
        {
            float w = (float)(Num(p, "width", 800.0) * 0.001);
            float h = (float)(Num(p, "height", 800.0) * 0.001);
            float th = (float)(Num(p, "thickness", 25.0) * 0.001);
            string key = "panel " + w.ToString("R") + "x" + h.ToString("R") + "x" + th.ToString("R");
            Mesh mesh;
            if (!cache.TryGetValue(key, out mesh))
            {
                mesh = SaveMesh(ProceduralMeshes.PanelFull(w, h, th, "EOS SAT-1 solar panel"), MeshFile("Solar panel"));
                cache[key] = mesh;
            }
            var mats = MiniJson.Obj(p, "materials") ?? new Dictionary<string, object>();
            var go = MeshPart(name, mesh, new[] { m.Model(Str(mats, "front", "panel_a")), m.Model(Str(mats, "back", "panel_back")),
                                                   m.Model(Str(mats, "edge", "bronze")) });
            Vec3d n = VecMm(p, "normal", -Vec3d.UnitX).Normalized;
            Vec3d up = Vec3d.RejectFrom(VecMm(p, "up", Vec3d.UnitZ), n).Normalized;
            go.transform.localPosition = U(VecMm(p, "center", Vec3d.Zero));
            // local +Z = cells, local +Y = up; the picture's right is then (up x normal), as in preview_model.py
            go.transform.localRotation = Quaternion.LookRotation(UDir(n), UDir(up));
            return go;
        }

        static GameObject BuildAxes(Transform parent, MeshSet ms, Mats m, float length)
        {
            var axes = Group("Body Axes", parent);
            Arrow(axes.transform, Vec3d.UnitX, m.AxisX, ms, "+X flight", new Color(1f, 0.55f, 0.5f), m, length);
            Arrow(axes.transform, Vec3d.UnitY, m.AxisY, ms, "+Y wing axis", new Color(0.55f, 1f, 0.6f), m, length);
            Arrow(axes.transform, Vec3d.UnitZ, m.AxisZ, ms, "+Z camera", new Color(0.6f, 0.75f, 1f), m, length);
            return axes;
        }

        static void Arrow(Transform parent, Vec3d axis, Material mat, MeshSet ms, string label, Color labelColor, Mats m, float length)
        {
            Cylinder("Shaft " + label, parent, SimToUnity.Vec(axis * (length * 0.43)), SimToUnity.Vec(axis), 0.014f, length * 0.86f, mat, true);
            var tip = new GameObject("Tip " + label);
            tip.transform.SetParent(parent, false);
            tip.transform.localPosition = SimToUnity.Vec(axis * (length * 0.86));
            tip.transform.localRotation = Quaternion.FromToRotation(Vector3.up, SimToUnity.Vec(axis));
            tip.transform.localScale = new Vector3(0.06f, 0.15f, 0.06f);
            tip.AddComponent<MeshFilter>().sharedMesh = ms.Cone;
            Plain(tip.AddComponent<MeshRenderer>(), mat);
            Text("Label " + label, parent, SimToUnity.Vec(axis * (length + 0.12)), label, 0.009f, labelColor, m.Hud,
                 TextAnchor.MiddleCenter, true);
        }

        // ------------------------------------------------------------------ project settings
        static void ConfigureProject()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.Android.forceInternetPermission = true;
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null)
            {
                PropertyInfo prop = rp.GetType().GetProperty("shadowDistance", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanRead && prop.CanWrite && prop.PropertyType == typeof(float))
                {
                    float current = (float)prop.GetValue(rp, null);
                    if (current > 12f)
                    {
                        prop.SetValue(rp, 10f, null);
                        EditorUtility.SetDirty(rp);
                        Debug.Log("EOSAT-1: set the render pipeline's shadow distance from " + current +
                                  " m to 10 m so the spacecraft gets crisp self-shadows.");
                    }
                }
            }
            else if (QualitySettings.shadowDistance > 12f)
            {
                QualitySettings.shadowDistance = 10f;
            }
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            // drop entries left by older builds whose demo scene has since been deleted
            int removed = list.RemoveAll(s => s.path != path && s.path.EndsWith("/EOSAT1_Demo.unity") && !System.IO.File.Exists(s.path));
            if (list.Exists(s => s.path == path))
            {
                if (removed > 0) EditorBuildSettings.scenes = list.ToArray();
                return;
            }
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
