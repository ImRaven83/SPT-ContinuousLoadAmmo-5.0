using System;
using ContinuousLoadAmmo.Utils;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ContinuousLoadAmmo.Controllers;

public class LoadAmmoUI
{
    private LoadAmmoController _loadAmmoController;
    private Transform _loadUITransform;
    private ItemViewLoadAmmoComponent _itemViewLoadAmmoComponent;
    private Image _magImage;
    private ItemIcon _itemIcon;
    private Il2CppSystem.Action _imageSubscription;
    private TextMeshProUGUI _magValue;

    // IL2CPP event handlers, kept so the same instances can be removed again
    private Il2CppSystem.Action<int> _ammoCountChangedHandler;
    private Il2CppSystem.Action _updateImageHandler;

    public void Initialize(Transform parent, LoadAmmoController loadAmmoController)
    {
        _loadAmmoController = loadAmmoController;
        SubscribeToController();
        if (_loadUITransform != null)
        {
            _loadUITransform.gameObject.SetActive(true);
            return;
        }

        PrepareGameObjects(parent);
        CloneTemplates();
    }

    private void SubscribeToController()
    {
        _loadAmmoController.OnStartLoading += HandleStart;
        _loadAmmoController.OnCloseInventoryLoading += Show;
        _loadAmmoController.OnEndLoading += Close;
        _ammoCountChangedHandler ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(UpdateTextValue);
        _loadAmmoController.PlayerInventoryController.add_OnAmmoLoaded(_ammoCountChangedHandler);
        _loadAmmoController.PlayerInventoryController.add_OnAmmoUnloaded(_ammoCountChangedHandler);
        _loadAmmoController.OnDispose += Dispose;
    }

    private void PrepareGameObjects(Transform parent)
    {
        // The params Type[] GameObject constructor takes IL2CPP types, add the components one by one instead
        GameObject loadAmmoObj = new(nameof(LoadAmmoUI));
        _loadUITransform = loadAmmoObj.AddComponent<RectTransform>();
        _loadUITransform.SetParent(parent);
        CommonUtils.SetUI(_loadUITransform);

        GameObject imageObj = new(nameof(Image));
        imageObj.AddComponent<RectTransform>().SetParent(_loadUITransform);
        CommonUtils.SetUI(imageObj.transform, new Vector2(0f, -150f), new Vector3(0.25f, 0.25f, 0.25f));
        _magImage = imageObj.AddComponent<Image>();
        _magImage.enabled = false;
    }

    private void CloneTemplates()
    {
        var gridItemView = ItemViewFactory.CreateFromPrefab<GridItemView>("grid_layout");
        var itemViewAnimation = gridItemView.Animator;
        var itemViewLoadAmmoComponentTemplate = itemViewAnimation._loadAmmoComponentTemplate;
        _itemViewLoadAmmoComponent = Object.Instantiate(itemViewLoadAmmoComponentTemplate, _loadUITransform, false);
        CommonUtils.SetUI(_itemViewLoadAmmoComponent.transform, new Vector2(0f, -150f), new Vector3(1.5f, 1.5f, 1.5f));

        var itemViewBottomPanelTemplate = gridItemView.BottomPanel;
        _magValue = Object.Instantiate(itemViewBottomPanelTemplate!.ItemValue, _loadUITransform, false);
        CommonUtils.SetUI(_magValue.transform, new Vector2(0f, -190f));
        _magValue.enableWordWrapping = false;
        _magValue.overflowMode = TextOverflowModes.Overflow;
        _magValue.alignment = TextAlignmentOptions.Center;
        _magValue.enabled = false;

        gridItemView.Kill();
    }

    private void HandleStart(float oneAmmoDuration, int ammoTotal, int ammoDone)
    {
        var cts = _itemViewLoadAmmoComponent._taskCancellation;
        cts?.Dispose();
        _itemViewLoadAmmoComponent.Show(oneAmmoDuration, ammoTotal, ammoDone);
    }

    private void Show(Item item)
    {
        _magValue.enabled = true;
        _magValue.text = _loadAmmoController.GetMagAmmoCountByLevel();

        GetImage(item);
    }

    private void GetImage(Item item)
    {
        _imageSubscription?.Invoke();
        _itemIcon = ItemViewFactory.LoadItemIcon(item);
        _updateImageHandler ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(UpdateImage);
        _imageSubscription = _itemIcon?.Changed.Bind(_updateImageHandler);
    }

    private void UpdateImage()
    {
        if (_itemIcon.Sprite == null) return;

        _magImage.sprite = _itemIcon.Sprite;
        _magImage.SetNativeSize();
        _magImage.enabled = true;
    }

    private void UpdateTextValue(int count)
    {
        if (_loadAmmoController.IsInventoryOpened) return;

        _magValue.SetText(_loadAmmoController.GetMagAmmoCountByLevel());
    }

    private void Close()
    {
        if (_itemViewLoadAmmoComponent != null)
        {
            var cts = _itemViewLoadAmmoComponent._taskCancellation;
            cts?.Cancel();
            _itemViewLoadAmmoComponent.gameObject.SetActive(false);
        }
        if (_magImage != null)
        {
            _magImage.enabled = false;
        }
        _imageSubscription?.Invoke();
        _imageSubscription = null;
        if (_magValue != null)
        {
            _magValue.enabled = false;
        }
    }

    public void Dispose()
    {
        Close();
        _loadUITransform.gameObject.SetActive(false);
#if DEBUG
        Object.Destroy(_loadUITransform.gameObject);
#endif
        if (_loadAmmoController is null) return;

        _loadAmmoController.OnStartLoading -= HandleStart;
        _loadAmmoController.OnCloseInventoryLoading -= Show;
        _loadAmmoController.OnEndLoading -= Close;
        _loadAmmoController.PlayerInventoryController.remove_OnAmmoLoaded(_ammoCountChangedHandler);
        _loadAmmoController.PlayerInventoryController.remove_OnAmmoUnloaded(_ammoCountChangedHandler);
        _loadAmmoController.OnDispose -= Dispose;
        _loadAmmoController = null;
    }
}
