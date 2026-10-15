using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Comfort.Common;
using ContinuousLoadAmmo.Controllers;
using ContinuousLoadAmmo.Models;
using ContinuousLoadAmmo.Utils;
using EFT;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace ContinuousLoadAmmo.Components;

/// <summary>
/// Injected into IL2CPP by <see cref="SPTushonka.Reflection.Il2Cpp.Il2CppInjection.RegisterAll"/>.
/// Members using managed-only types are hidden from IL2CPP, they are only ever called from managed code
/// </summary>
public class QuickAmmoSelector : InputNode
{
    private readonly List<GridItemView> _ammoViews = [];
    private readonly List<Ammo> _ammoItems = [];
    private readonly HashSet<string> _seenAmmoTplScratch = [];
    private EmptyItemContext _emptyItemContext;
    private LoadAmmoController _loadAmmoControllerController;
    private TaskCompletionSource<Ammo> _chosenAmmoTcs;

    private Transform _cancelView;
    private Image _backgroundColor;

    public QuickAmmoSelector(IntPtr pointer) : base(pointer)
    {
    }

    public bool IsShown => _chosenAmmoTcs is not null;

    private int _index;

    private int Index
    {
        get => _index;
        set
        {
            if (value == _index) return;

            HighlightIndex(_index, value);
            _index = value;
        }
    }

    [HideFromIl2Cpp]
    public static QuickAmmoSelector Create(Transform parent, LoadAmmoController loadAmmoControllerController)
    {
        // The params Type[] GameObject constructor takes IL2CPP types, add the components one by one instead
        var gameObject = new GameObject(nameof(QuickAmmoSelector));
        var rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.SetParent(parent);

        CommonUtils.SetUI(rectTransform, new Vector2(0, -150f));
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(1f, 0.5f);

        var layoutGroup = gameObject.AddComponent<HorizontalLayoutGroup>();
        layoutGroup.spacing = 4f;
        layoutGroup.childControlWidth = false;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childAlignment = TextAnchor.MiddleCenter;

        var quickAmmoSelector = gameObject.AddComponent<QuickAmmoSelector>();
        quickAmmoSelector._emptyItemContext = new EmptyItemContext();
        quickAmmoSelector._loadAmmoControllerController = loadAmmoControllerController;
        quickAmmoSelector._loadAmmoControllerController.OnDispose += quickAmmoSelector.DestroySelf;
        quickAmmoSelector.CreateCancelView();
        CommonUtils.InputTree.Add(quickAmmoSelector);
        return quickAmmoSelector;
    }

    public void Update()
    {
        // Transfer to TranslateCommand, our custom hotkey _may_ not be an ECommand
        if (Input.GetKeyUp(ContinuousLoadAmmo.QuickLoadHotkey.Value.MainKey))
        {
            TranslateCommand(ECommand.BeginSpecialInteracting); // No other uses?
        }
    }

    public override ETranslateResult TranslateCommand(ECommand command)
    {
        if (!_loadAmmoControllerController.CanLoadOutsideInventory)
        {
            return ETranslateResult.Ignore;
        }

        if (IsShown)
        {
            if (command.IsCommand(ECommand.ScrollNext))
            {
                Next();
                return ETranslateResult.Block;
            }

            if (command.IsCommand(ECommand.ScrollPrevious))
            {
                Previous();
                return ETranslateResult.Block;
            }

            // Select ammo
            if (Input.GetKeyUp(ContinuousLoadAmmo.QuickLoadHotkey.Value.MainKey)
                && command.IsCommand(ECommand.BeginSpecialInteracting)) // Only transferred from update to avoid duplicates
            {
                SetChosenAmmo(GetSelectedAmmo());
                Close();
                return ETranslateResult.Block;
            }
            return ETranslateResult.Ignore;
        }

        if (!_loadAmmoControllerController.IsInventoryOpened)
        {
            if (_loadAmmoControllerController.IsActive)
            {
                // Cancel on shoot/alt shoot, or quick load key, if loading ammo outside inventory
                if (command.IsCommand(ECommand.ToggleShooting)
                    || command.IsCommand(ECommand.ToggleAlternativeShooting)
                    || (Input.GetKeyUp(ContinuousLoadAmmo.QuickLoadHotkey.Value.MainKey)
                        && command.IsCommand(ECommand.BeginSpecialInteracting)))
                {
                    _loadAmmoControllerController.StopLoading();
                    return ETranslateResult.Block;
                }

                return ETranslateResult.Ignore;
            }

            if (Input.GetKey(ContinuousLoadAmmo.QuickLoadHotkey.Value.MainKey)
                && (command.IsCommand(ECommand.ScrollNext) || command.IsCommand(ECommand.ScrollPrevious)))
            {
                _ = OpenAmmoSelectorAsync();
                return ETranslateResult.Block;
            }
        }

        if (Input.GetKeyUp(ContinuousLoadAmmo.QuickLoadHotkey.Value.MainKey)
            && command.IsCommand(ECommand.BeginSpecialInteracting)) // Only transferred from update to avoid duplicates
        {
            if (_loadAmmoControllerController.IsActive)
            {
                _loadAmmoControllerController.StopLoading();
                return ETranslateResult.Block;
            }

            switch (ContinuousLoadAmmo.QuickLoadMode.Value)
            {
                case QuickLoadMode.HighestPenetration:
                case QuickLoadMode.LastBulletMagazine:
                    _loadAmmoControllerController.TryQuickLoadAmmo();
                    break;
                case QuickLoadMode.LastMagazinePreset:
                    _loadAmmoControllerController.TryQuickLoadLastPreset();
                    break;
                default:
                    return ETranslateResult.Ignore;
            }
            return ETranslateResult.Block;
        }
        return ETranslateResult.Ignore;
    }

    public override void TranslateAxes(ref Il2CppStructArray<float> axes)
    {
    }

    public override ECursorResult ShouldLockCursor()
    {
        return ECursorResult.Ignore;
    }

    public void OnDestroy()
    {
        if (IsShown)
        {
            Close();
        }
        _loadAmmoControllerController.OnDispose -= DestroySelf;
        CommonUtils.InputTree.Remove(this);
    }

    [HideFromIl2Cpp]
    private async Task OpenAmmoSelectorAsync()
    {
        if (!_loadAmmoControllerController.IsQuickLoadAvailable(out var reachableAmmo, out var foundMagazine))
        {
            return;
        }

        // Only show one of each type
        _seenAmmoTplScratch.Clear();
        reachableAmmo.RemoveAll(ShouldRemoveFromList);

        var chosenAmmo = await ShowAcceptableAmmoAsync(reachableAmmo, _loadAmmoControllerController.PlayerInventoryController);
        if (chosenAmmo is not null)
        {
            _ = _loadAmmoControllerController.LoadMagazineAsync(chosenAmmo, foundMagazine);
        }
    }

    private bool ShouldRemoveFromList(Ammo ammo)
    {
        return !_seenAmmoTplScratch.Add(ammo.TemplateId.ToString());
    }

    [HideFromIl2Cpp]
    [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks")]
    private Task<Ammo> ShowAcceptableAmmoAsync(List<Ammo> foundAmmo, InventoryController inventoryController) // method_5
    {
        foreach (var ammo in foundAmmo)
        {
            var view = GridItemView.Create(
                ammo,
                _emptyItemContext,
                ItemRotation.Horizontal,
                inventoryController,
                inventoryController,
                null,
                null,
                null,
                null,
                null
            );
            AddAmmoView(view, ammo);
        }
        _cancelView.gameObject.SetActive(true);
        _cancelView.SetAsLastSibling();

        _index = 0;
        HighlightIndex(_index, 0);

        SetChosenAmmo(null);
        _chosenAmmoTcs = new TaskCompletionSource<Ammo>();
        return _chosenAmmoTcs.Task;
    }

    private void SetChosenAmmo(Ammo ammo)
    {
        _chosenAmmoTcs?.SetResult(ammo);
        _chosenAmmoTcs = null;
    }

    [HideFromIl2Cpp]
    private Ammo GetSelectedAmmo()
    {
        return _index == _ammoItems.Count
            ? null // Cancel/no option is selected
            : _ammoItems[_index];
    }

    public void Close()
    {
        HighlightAmmoView(_index, false);
        for (var i = _ammoViews.Count - 1; i >= 0; i--)
        {
            HighlightAmmoView(i, false);
            RemoveAmmoView(i);
        }
        _cancelView.gameObject.SetActive(false);
        SetChosenAmmo(null);
    }

    private void Previous()
    {
        var num = _ammoViews.Count + 1;
        Index = (Index + 1) % num;
    }

    private void Next()
    {
        var num = _ammoViews.Count + 1;
        Index = ((Index - 1) + num) % num;
    }

    private void HighlightIndex(int prevSelectionIndex, int currentSelectionIndex)
    {
        HighlightAmmoView(prevSelectionIndex, false);
        HighlightAmmoView(currentSelectionIndex, true);
    }

    private void HighlightAmmoView(int index, bool isSelected)
    {
        if (index < _ammoViews.Count)
        {
            _ammoViews[index].Highlight(isSelected);
        }
        else if (index == _ammoViews.Count)
        {
            _backgroundColor.color = isSelected ? Color.red : Color.grey;
        }
    }

    [HideFromIl2Cpp]
    public void AddAmmoView(GridItemView view, Ammo ammo)
    {
        var layoutElement = view.gameObject.AddComponent<LayoutElement>();
        var rectTransform = view.GetComponent<RectTransform>();
        var sizeDelta = rectTransform.sizeDelta;
        layoutElement.preferredWidth = sizeDelta.x;
        layoutElement.preferredHeight = sizeDelta.y;
        rectTransform.SetParent(transform, worldPositionStays: false);
        _ammoViews.Add(view);
        _ammoItems.Add(ammo);
        view.gameObject.SetActive(true);
    }

    public void RemoveAmmoView(int index)
    {
        var gridItemView = _ammoViews[index];
        gridItemView.transform.SetParent(null);
        Destroy(gridItemView.gameObject.GetComponent<LayoutElement>());
        _ammoViews.RemoveAt(index);
        _ammoItems.RemoveAt(index);
        gridItemView.Kill();
    }

    /// <summary>
    /// Clone EftBattleUIScreen._cancelView for QuickAmmoSelector's use
    /// </summary>
    private void CreateCancelView()
    {
        var battleUiScreen = Singleton<CommonUI>.Instance.EftBattleUIScreen;
        _cancelView = Instantiate(battleUiScreen.AmmoSelector._cancelView, transform, false).transform;
        _cancelView.localPosition = Vector3.zero;
        _backgroundColor = _cancelView.Find("Image").GetComponent<Image>();
        _cancelView.gameObject.SetActive(false);
    }

    private void DestroySelf()
    {
        Destroy(this);
    }
}
