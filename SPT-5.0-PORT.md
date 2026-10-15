# SPT 5.0 port notes

SPT 5.0 (Single Player Tushonka) runs EFT 1.1 under **IL2CPP** with **BepInEx 6**, so this is a platform port, not a version bump.

## What changed

- **Build**: targets `net10.0`. It references BepInEx 6 (`BepInEx\core`), the interop assemblies BepInEx generates in `BepInEx\interop` on first launch, and `SPTushonka.Reflection` (`BepInEx\plugins\sptushonka`). Launch the game once before building so the interop folder exists.
- **Plugin**: `BaseUnityPlugin`/`Awake` became `BasePlugin`/`Load`, `Logger` became `Log`, and the hot-reload `OnDestroy` became `Unload`. It now depends on `com.sptushonka.core`, which installs the main-thread dispatcher that `await` continuations resume on.
- **Patching**: `SPT.Reflection.*` moved to `SPTushonka.Reflection.*`. The API is the same.
- **IL2CPP idioms**:
  - Type checks on game objects use `TryCast<T>()`, because `is`/`as` only see the wrapper's declared type.
  - Object identity compares native pointers: `IsSameObject`, `ContainsObject`.
  - `MongoID` values are compared by string (`IsSameId`), since `MongoID` is a boxed struct.
  - Game events are subscribed with `add_X`/`remove_X` and delegates converted through `DelegateSupport.ConvertDelegate`. The converted instances are kept so unsubscribing works.
  - Game `Task`s are bridged with `AsManaged()`, and `Il2CppSystem.Threading.Tasks.Task` is used where a patch replaces `__result`.
  - Harmony `____field` injection is replaced by the interop property on `__instance`.
  - `new GameObject(name, typeof(...))` is replaced by `AddComponent<T>()`.
- **`QuickAmmoSelector`** is injected into IL2CPP. It is registered via `Il2CppInjection.RegisterAll` and has an `IntPtr` constructor. Managed-only members are marked `[HideFromIl2Cpp]`, and `TranslateAxes` takes `ref Il2CppStructArray<float>`.
- **`LoadingEndPatches`**: interop interfaces are generated as classes, so the managed interface map is empty. `IMagazineLoadingProcess` implementers are now found through IL2CPP type assignability.
- **Quick load lookup**: EFT's `InventoryController.GetAcceptableItemsNonAlloc` takes IL2CPP lists and delegates in 1.1. It is replaced by the managed, non-recursive `GetTopLevelItemsNonAlloc`.
- **Last-preset store**: moved from EFT's Newtonsoft (now an IL2CPP assembly) to `System.Text.Json`. The file format is unchanged: preset IDs are stored as strings.
- **UI Fixes interop**: uses `IL2CPPChainloader` and requires UI Fixes **7.0.0+** (its SPT 5.0 line). It reads `_totalTask` by reflection, so it works whether UI Fixes ends up using a managed or an IL2CPP completion source.

## Verify against the 1.1 interop assemblies

This was ported without access to an EFT 1.1 install, so nothing here has been compiled against the game. Expect a round of compile fixes for member names and signatures that changed between the 4.x client and 1.1, most likely in these places:

- `GridItemView.Create(...)` and `subInteractionsWrapper.SetSubInteractions(...)`. Under interop, interface-typed parameters need an explicit `.Cast<IInterface>()`.
- `NotificationManager.DisplayMessageNotification` signature. The call follows Fika's 5.0 branch.
- Remapped type names this mod uses (`Magazine`, `Ammo`, `ContainerCollection`, `MagPreset`, `InventoryEquipment`, ...), because 1.1 names come from IL2CPP metadata, not SPT's remapper.
- Whether interop events are exposed as `add_`/`remove_` methods (assumed) or as C# events.
