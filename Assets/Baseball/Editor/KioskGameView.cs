// Al dar Play, la vista Game se pone en 1080 × 1920 (la pantalla vertical del tótem), para
// ver el juego como se verá en el kiosco. Crea ese tamaño si todavía no existe.

using System;
using System.Reflection;
using UnityEditor;

namespace Baseball.EditorTools
{
    [InitializeOnLoad]
    public static class KioskGameView
    {
        const string Label = "Kiosco 1080x1920";

        static KioskGameView()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.EnteredPlayMode) Select(); };
        }

        [MenuItem("Baseball/Vista Game en 1080x1920")]
        public static void Select()
        {
            try
            {
                var asm = typeof(Editor).Assembly;
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var instance = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
                var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { (int)GameViewSizeGroupType.Standalone });
                var groupType = group.GetType();
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null), index = -1;
                for (int i = 0; i < count; i++)
                {
                    var s = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                    if ((string)sizeType.GetProperty("baseText").GetValue(s) == Label) index = i;
                }
                if (index < 0)
                {
                    var kind = Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
                    var size = sizeType.GetConstructor(new[] { kind.GetType(), typeof(int), typeof(int), typeof(string) }).Invoke(new[] { kind, 1080, 1920, (object)Label });
                    groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                    index = count;
                }
                var gameView = EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"), false, null, false);
                gameView.GetType().GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(gameView, new object[] { index, null });
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning($"[Kiosco] No se pudo poner la vista Game en 1080x1920: {e.Message}"); }
        }
    }
}
