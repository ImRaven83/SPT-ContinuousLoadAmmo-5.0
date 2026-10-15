using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using ContinuousLoadAmmo.Utils;
using EFT.InventoryLogic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
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
    /// Interop interfaces are generated as classes, so managed interface maps are empty. Implementers are found by
    /// the interface method's signature instead, using plain reflection only. Never resolve the IL2CPP type here
    /// (Il2CppType.From and friends): that runs the interop static constructor, which calls il2cpp_runtime_class_init
    /// and runs the game's static constructors at plugin load, before backend globals exist. A type initializer that
    /// fails then stays broken (RepairController did, which stalled the game before the main menu)
    /// </remarks>
    private static List<MethodBase> TargetMethods()
    {
        var methods = new List<MethodBase>();

        var startMethod = typeof(IMagazineLoadingProcess).GetMethod(nameof(IMagazineLoadingProcess.Start));
        var returnType = startMethod!.ReturnType;
        var gameAssembly = typeof(IMagazineLoadingProcess).Assembly;

        foreach (var type in AccessTools.GetTypesFromAssembly(gameAssembly))
        {
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters
                || !typeof(Il2CppObjectBase).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // Implicit implementations are named Start, explicit ones keep the interface name as a prefix
                var isStart = method.Name == nameof(IMagazineLoadingProcess.Start)
                              || method.Name.EndsWith($"{nameof(IMagazineLoadingProcess)}_{nameof(IMagazineLoadingProcess.Start)}", StringComparison.Ordinal);
                if (isStart && method.ReturnType == returnType && method.GetParameters().Length == 0)
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
