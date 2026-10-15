using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Version = SemanticVersioning.Version;

namespace ContinuousLoadAmmo.Utils;

internal static class MultiSelectInterop
{
    // First UI Fixes major built for SPT 5.0
    private static readonly Version _requiredVersion = new(7, 0, 0);

    private static bool? _uiFixesLoaded;
    private static Func<object> _loadUnloadSerializerGetter;
    private static FieldInfo _totalTaskField;
    private static PropertyInfo _taskProperty;
    private static PropertyInfo _isCompletedProperty;
    private static MethodInfo _stopLoadingMethod;

    public static bool MultiSelectLoadSerializerIsActive
    {
        get
        {
            if (!Loaded()) return false;

            var serializer = _loadUnloadSerializerGetter.Invoke();
            if (serializer is null) return false;

            // The completion source may be a managed or an IL2CPP type depending on the UI Fixes build, so read it by reflection
            var totalTask = _totalTaskField.GetValue(serializer);
            if (totalTask is null) return false;

            _taskProperty ??= totalTask.GetType().GetProperty("Task");
            var task = _taskProperty?.GetValue(totalTask);
            if (task is null) return false;

            _isCompletedProperty ??= task.GetType().GetProperty("IsCompleted");
            return _isCompletedProperty?.GetValue(task) is false;
        }
    }

    public static MethodInfo StopLoadingMethod => Loaded() ? _stopLoadingMethod : null;

    private static bool Loaded()
    {
        if (_uiFixesLoaded.HasValue) return _uiFixesLoaded.Value;

        var present = IL2CPPChainloader.Instance.Plugins.TryGetValue("com.tyfon.uifixes", out var pluginInfo);
        var correctVersion = present && pluginInfo.Metadata.Version >= _requiredVersion;
        _uiFixesLoaded = present && correctVersion;

        if (_uiFixesLoaded.Value)
        {
            try
            {
                var multiSelectType = Type.GetType("UIFixes.MultiSelect, Tyfon.UIFixes");
                var taskSerializerType = Type.GetType("UIFixes.MultiSelectItemContextTaskSerializer, Tyfon.UIFixes");
                var loadUnloadSerializerMethod = AccessTools.PropertyGetter(multiSelectType, "LoadUnloadSerializer");
                _loadUnloadSerializerGetter = AccessTools.MethodDelegate<Func<object>>(loadUnloadSerializerMethod);
                _stopLoadingMethod = AccessTools.Method(multiSelectType, "StopLoading");
                _totalTaskField = AccessTools.Field(taskSerializerType, "_totalTask");

                if (_loadUnloadSerializerGetter is null || _stopLoadingMethod is null || _totalTaskField is null)
                {
                    throw new InvalidOperationException("UI Fixes interop: Required method or field could not be found.");
                }

                L.Info("UI Fixes interop loaded successfully");
            }
            catch (Exception ex)
            {
                L.Warning(
                    $"UI Fixes {pluginInfo!.Metadata.Version} is present but something went wrong, interop will not work\n{ex}"
                );
                _uiFixesLoaded = false;
            }
        }

        if (present && !correctVersion)
        {
            L.Warning(
                $"UI Fixes {pluginInfo.Metadata.Version} is present but minimum version: {_requiredVersion} is required, interop will not work"
            );
        }

        return _uiFixesLoaded.Value;
    }
}
