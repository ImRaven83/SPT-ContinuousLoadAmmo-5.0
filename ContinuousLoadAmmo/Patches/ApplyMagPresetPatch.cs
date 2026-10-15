using System;
using System.Collections.Generic;
using System.Reflection;
using ContinuousLoadAmmo.Utils;
using Diz.LanguageExtensions;
using EFT.Builds;
using EFT.InventoryLogic;
using EFT.UI;
using SPTushonka.Reflection.Il2Cpp;
using SPTushonka.Reflection.Patching;

namespace ContinuousLoadAmmo.Patches;

public class ApplyMagPresetPatch : ModulePatch
{
    public static event Action<MagPreset, List<Magazine>> OnApplyMagPreset;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemUiContext).GetMethod(nameof(ItemUiContext.ApplyMagPreset));
    }

    [PatchPrefix]
    protected static bool Prefix(
        ItemUiContext __instance,
        MagPreset preset,
        Il2CppSystem.Collections.Generic.IReadOnlyCollection<Magazine> magazines,
        ref Il2CppSystem.Threading.Tasks.Task<Option> __result
    )
    {
        ProfileMagazinePresetStore.UpdateMagPreset(preset);

        if (!CommonUtils.InRaid) return true;

        // Task only used by mag presets window, which we disable in-raid
        __result = Il2CppSystem.Threading.Tasks.Task.FromResult(new Option());

        // Interop interfaces do not inherit each other, cast to IEnumerable to iterate
        var magazineList = magazines.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Magazine>>().ToManagedList();
        OnApplyMagPreset?.Invoke(preset, magazineList);
        return false;
    }
}
