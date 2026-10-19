
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SaveToolkit.Editor
{
    /// <summary>
    /// Adds SAVETOOLKIT_MESSAGEPACK when a MessagePack assembly is loaded in the
    /// Editor (and removes it when it disappears), which compiles the optional
    /// SaveToolkit.MessagePack assembly. Auto detection can be turned off from
    /// Tools/Save Toolkit/MessagePack Module.
    /// </summary>
    [InitializeOnLoad]
    internal static class MessagePackModuleDetector
    {
        private const string Define = SaveSerializerRegistry.MessagePackDefine;
        private const string AutoDetectKey = "SaveToolkit.MessagePack.AutoDetect";
        private const string MenuRoot = "Tools/Save Toolkit/MessagePack Module/";

        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.WebGL
        };

        static MessagePackModuleDetector()
        {
            EditorApplication.delayCall += AutoDetect;
        }

        private static bool AutoDetectEnabled
        {
            get => EditorPrefs.GetBool(AutoDetectKey, true);
            set => EditorPrefs.SetBool(AutoDetectKey, value);
        }

        private static void AutoDetect()
        {
            if (AutoDetectEnabled)
                SetDefine(IsMessagePackLoaded());
        }

        private static bool IsMessagePackLoaded()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "MessagePack")
                    return true;
            }

            return false;
        }

        private static void SetDefine(bool enabled)
        {
            var targets = new HashSet<NamedBuildTarget>(Targets)
            {
                NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)
            };

            bool changed = false;

            foreach (NamedBuildTarget target in targets)
            {
                string current;

                try
                {
                    current = PlayerSettings.GetScriptingDefineSymbols(target);
                }
                catch (Exception)
                {
                    continue; // Target not supported by this Editor install.
                }

                var defines = new List<string>(current.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                bool has = defines.Contains(Define);

                if (has == enabled)
                    continue;

                if (enabled)
                    defines.Add(Define);
                else
                    defines.Remove(Define);

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                changed = true;
            }

            if (changed)
                Debug.Log(enabled
                    ? $"[SaveToolkit] MessagePack found: added {Define}; the MessagePack module will compile."
                    : $"[SaveToolkit] MessagePack not found: removed {Define}.");
        }

        [MenuItem(MenuRoot + "Auto Detect", false, 100)]
        private static void ToggleAutoDetect()
        {
            AutoDetectEnabled = !AutoDetectEnabled;
            AutoDetect();
        }

        [MenuItem(MenuRoot + "Auto Detect", true)]
        private static bool ToggleAutoDetectValidate()
        {
            Menu.SetChecked(MenuRoot + "Auto Detect", AutoDetectEnabled);
            return true;
        }

        [MenuItem(MenuRoot + "Enable", false, 111)]
        private static void Enable()
        {
            AutoDetectEnabled = false;
            SetDefine(true);
        }

        [MenuItem(MenuRoot + "Disable", false, 112)]
        private static void Disable()
        {
            AutoDetectEnabled = false;
            SetDefine(false);
        }
    }
}
