using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Builds;
using EFT.Communications;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

#pragma warning disable CS0618 // Type or member is obsolete

namespace ContinuousLoadAmmo.Utils;

public static class CommonUtils
{
    public static bool InRaid => InGameStatus.InRaid;

    public static Transform EftBattleUIScreenTransform
    {
        get
        {
            if (field != null) return field;

            field = Singleton<CommonUI>.Instance.EftBattleUIScreen.transform;
            return field;
        }
    }

    public static InputTree InputTree
    {
        get
        {
            // Thanks Fika team/Lacyway!
            if (field != null) return field;

            var inputObj = GameObject.Find("___Input");
            if (inputObj == null)
            {
                throw new NullReferenceException("Could not find InputTree object!");
            }

            field = inputObj.GetComponent<InputTree>();
            return field;
        }
    }

    /// <summary>
    /// Check if magazine has ammo that doesn't match ammoToLoad's caliber
    /// </summary>
    public static bool HasAmmoWithDifferentCaliber(this Magazine magazine, Ammo ammoToLoad)
    {
        foreach (var cartridge in magazine.Cartridges._items)
        {
            var cartridgeAmmo = cartridge?.TryCast<Ammo>();
            if (cartridgeAmmo is null) continue;

            if (cartridgeAmmo.Caliber != ammoToLoad.Caliber) return true;
        }
        return false;
    }

    /// <summary>
    /// Get acceptable items recursively
    /// </summary>
    public static void GetAcceptableItemsNonAlloc<TItem>(
        this InventoryEquipment inventoryEquipment,
        EquipmentSlot[] equipmentSlots,
        List<TItem> preAllocatedList,
        Predicate<TItem> predicate = null,
        Predicate<ContainerCollection> goDeeperPredicate = null
    ) where TItem : Item
    {
        foreach (var equipmentSlot in equipmentSlots)
        {
            var parentContainer = inventoryEquipment.GetSlot(equipmentSlot).ContainedItem?.TryCast<ContainerCollection>();
            if (parentContainer is null || (goDeeperPredicate is not null && !goDeeperPredicate(parentContainer)))
            {
                continue;
            }

            parentContainer.CollectItems(preAllocatedList, predicate, goDeeperPredicate);
        }
    }

    /// <summary>
    /// Get acceptable items of the top level containers only, non-recursive.
    /// Managed replacement for InventoryController.GetAcceptableItemsNonAlloc, which takes IL2CPP collections in 5.0
    /// </summary>
    public static void GetTopLevelItemsNonAlloc<TItem>(
        this InventoryEquipment inventoryEquipment,
        EquipmentSlot[] equipmentSlots,
        List<TItem> preAllocatedList,
        Predicate<TItem> predicate = null,
        Predicate<ContainerCollection> containerPredicate = null
    ) where TItem : Item
    {
        foreach (var equipmentSlot in equipmentSlots)
        {
            var parentContainer = inventoryEquipment.GetSlot(equipmentSlot).ContainedItem?.TryCast<ContainerCollection>();
            if (parentContainer is null || (containerPredicate is not null && !containerPredicate(parentContainer)))
            {
                continue;
            }

            foreach (var container in parentContainer.Containers)
            {
                foreach (var item in container.Items)
                {
                    var genericItem = item.TryCast<TItem>();
                    if (genericItem is not null && (predicate is null || predicate(genericItem)))
                    {
                        preAllocatedList.Add(genericItem);
                    }
                }
            }
        }
    }

    public static void GetAllItemsOfContainer<TItem>(
        this ContainerCollection parentContainer,
        List<TItem> preAllocatedList,
        Predicate<TItem> predicate = null,
        Predicate<ContainerCollection> goDeeperPredicate = null
    ) where TItem : Item
    {
        parentContainer.CollectItems(preAllocatedList, predicate, goDeeperPredicate);
    }

    private static void CollectItems<TItem>(
        this ContainerCollection parentContainer,
        List<TItem> preAllocatedList,
        Predicate<TItem> predicate,
        Predicate<ContainerCollection> goDeeperPredicate
    ) where TItem : Item
    {
        foreach (var container in parentContainer.Containers)
        {
            foreach (var item in container.Items)
            {
                // IL2CPP hands back wrappers typed as the declared type, so type checks have to go through TryCast
                var childContainer = item.TryCast<ContainerCollection>();
                if (childContainer is not null && (goDeeperPredicate is null || goDeeperPredicate(childContainer)))
                {
                    childContainer.CollectItems(preAllocatedList, predicate, goDeeperPredicate);
                }

                var genericItem = item.TryCast<TItem>();
                if (genericItem is not null && (predicate is null || predicate(genericItem)))
                {
                    preAllocatedList.Add(genericItem);
                }
            }
        }
    }

    public static bool HasAnyHandsActionNonLinq(this ItemController itemController)
    {
        foreach (var eventArgs in itemController.ActiveEvents)
        {
            if (eventArgs.TryCast<IItemInHandsEventArgs>() is not null && eventArgs.TryCast<RemoveFromHandsEventArgs>() is null)
            {
                // RemoveFromHandsEventArgs - not considered as busy hands, for successive unloading of different bullets
                return true;
            }
        }
        return false;
    }

    public static void DisplayNotification(
        string message,
        ENotificationIconType iconType = ENotificationIconType.Default,
        bool alwaysDisplay = false,
        ENotificationDurationType duration = ENotificationDurationType.Default
    )
    {
        if (ContinuousLoadAmmo.QuickLoadNotify.Value || alwaysDisplay)
        {
            NotificationManager.DisplayMessageNotification(message, duration, iconType, new Il2CppSystem.Nullable<Color>(), false);
        }
    }

    public static string DisplayText(this MagPreset preset)
    {
        return $"{preset.Name} ({preset.Caliber.Replace("Caliber", string.Empty)})";
    }

    /// <summary>
    /// PP-9 Klin special case
    /// </summary>
    public static string GetWeaponCaliber(this Weapon weapon)
    {
        var ammoCaliber = weapon.AmmoCaliber;
        return ammoCaliber == "9x18PMM" ? "9x18PM" : ammoCaliber;
    }

    public static string GetCaliberReally(this MagPreset preset)
    {
        return preset.Caliber.Replace("Caliber", string.Empty);
    }

    /// <summary>
    /// Two wrappers can point at the same IL2CPP object, so compare the native pointers
    /// </summary>
    public static bool IsSameObject(this Il2CppObjectBase a, Il2CppObjectBase b)
    {
        if (a is null || b is null) return a is null && b is null;

        return a.Pointer == b.Pointer;
    }

    public static bool ContainsObject<T>(this List<T> list, T item) where T : Il2CppObjectBase
    {
        foreach (var listItem in list)
        {
            if (listItem.IsSameObject(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// MongoID is a boxed struct under IL2CPP, compare by value instead of relying on operator overloads
    /// </summary>
    public static bool IsSameId(this MongoID a, MongoID b)
    {
        if (a is null || b is null) return a is null && b is null;

        return a.ToString() == b.ToString();
    }

    public static void SetUI(Transform transform, Vector2? offset = null, Vector3? scale = null)
    {
        var rectTransform = transform.Cast<RectTransform>();
        rectTransform.anchoredPosition = offset ?? Vector2.zero;
        rectTransform.localScale = scale ?? Vector3.one;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }
}
