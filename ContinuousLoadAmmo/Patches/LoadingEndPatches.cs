using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using ContinuousLoadAmmo.Utils;
using EFT.InventoryLogic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using JetBrains.Annotations;
using SPTushonka.Reflection.Il2Cpp;

#pragma warning disable VSTHRD003

namespace ContinuousLoadAmmo.Patches;

public class LoadingEndPatches
{
    public static event Action OnLoadingEnd;
    private static Harmony _harmony;

    public static void Enable()
    {
        _harmony?.UnpatchSelf();
        _harmony = new Harmony($"{nameof(ContinuousLoadAmmo)}.{nameof(LoadingEndPatches)})");
        _harmony.CreateClassProcessor(typeof(LoadingEndPatches), true).Patch();
        L.Info($"Enabled patch {nameof(LoadingEndPatches)}");
    }

    public static void Disable()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        L.Info($"Disabled patch {nameof(LoadingEndPatches)}");
    }

    /// <summary>
    /// Get all methods implementing <see cref="IMagazineLoadingProcess.Start"/> and patch those
    /// </summary>
    /// <remarks>
    /// Interop interfaces are generated as classes, so managed interface maps are empty.
    /// Implementers are found through the IL2CPP type system instead, then the interop method is matched by name
    /// </remarks>
    [UsedImplicitly]
    private static List<MethodBase> TargetMethods()
    {
        var methods = new List<MethodBase>();

        var interfaceType = Il2CppType.Of<IMagazineLoadingProcess>();
        var gameAssembly = typeof(IMagazineLoadingProcess).Assembly;

        foreach (var type in AccessTools.GetTypesFromAssembly(gameAssembly))
        {
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(Il2CppObjectBase).IsAssignableFrom(type))
            {
                continue;
            }

            try
            {
                var il2CppType = type.ToIl2Cpp();
                if (il2CppType is null || il2CppType.IsInterface || il2CppType.IsAbstract || !interfaceType.IsAssignableFrom(il2CppType))
                {
                    continue;
                }
            }
            catch
            {
                // Not every interop type maps back to an IL2CPP class
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // Implicit implementations are named Start, explicit ones keep the interface name as a prefix
                var isStart = method.Name == nameof(IMagazineLoadingProcess.Start)
                              || method.Name.EndsWith($"{nameof(IMagazineLoadingProcess)}_{nameof(IMagazineLoadingProcess.Start)}", StringComparison.Ordinal);
                if (isStart && method.GetParameters().Length == 0)
                {
                    methods.Add(method);
                }
            }
        }

        foreach (var method in methods)
        {
            L.Info($"Found {method.DeclaringType}.{method.Name}");
        }
        return methods;
    }

    [UsedImplicitly]
    public static void Postfix(Il2CppSystem.Threading.Tasks.Task<IResult> __result)
    {
        if (__result is null) return;

        _ = WaitForEndAsync(__result.AsManaged());
    }

    private static async Task WaitForEndAsync(Task<IResult> result)
    {
        await result;

        // Resumed on the main thread by the SPTushonka dispatcher, safe to touch game objects
        OnLoadingEnd?.Invoke();
    }
}
