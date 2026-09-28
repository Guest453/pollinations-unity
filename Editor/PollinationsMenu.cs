// Editor utilities: a one-click "verify installation" menu plus helpers to open
// the sample scene. Only compiled in the Editor (see the asmdef's includePlatforms).
#if UNITY_EDITOR
using UnityEditor;

namespace Pollinations.Unity.Editor
{
    public static class PollinationsMenu
    {
        [MenuItem("Pollinations/Verify Installation")]
        public static void Verify()
        {
            bool ok = typeof(PollinationsClient) != null
                      && typeof(PollinationsAuth) != null
                      && typeof(PollinationsModels) != null;
            EditorUtility.DisplayDialog(
                "Pollinations for Unity",
                ok ? "Package is installed and references resolve.\n\nOpen Samples~/DemoScene for a working example."
                   : "Package failed to load — check the Console for compile errors.",
                "OK");
        }
    }
}
#endif
