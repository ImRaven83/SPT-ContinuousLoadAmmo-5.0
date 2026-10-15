using System;
using System.Reflection;
using ContinuousLoadAmmo.Utils;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using SPTushonka.Reflection.Patching;

namespace ContinuousLoadAmmo.Patches;

public class InventoryScreenClosePatch : ModulePatch
{
    public static event Action OnInventoryClose;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(InventoryScreen).GetMethod(nameof(InventoryScreen.Close));
    }

    /// <summary>
    /// UI, Patch to NOT stop loading ammo on close
    /// </summary>
    [PatchPrefix]
    protected static void Prefix(InventoryScreen __instance)
    {
        if (!CommonUtils.InRaid) return;

        // Harmony field injection does not work on IL2CPP types, fields are interop properties instead
        if (__instance._inventoryController?.TryCast<Player.PlayerInventoryController>() is { } playerInventoryController)
        {
            // It looks like only Load/UnloadMagazine checks for process locked, this should be fine
            playerInventoryController.SetNextProcessLocked(false);
        }

        // Skip StopProcesses and SetNextProcessLocked(true) after prefix
        __instance._inventoryController = null;

        OnInventoryClose?.Invoke();
    }
}
