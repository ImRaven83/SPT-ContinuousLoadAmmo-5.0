using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using EFT;
using EFT.Builds;
using SPTushonka.Reflection.Utils;

namespace ContinuousLoadAmmo.Utils;

public static class ProfileMagazinePresetStore
{
    private const string StoreFilename = "lastMagPresets.json";

    private static readonly string _modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string _pathToStoreFile = Path.Combine(_modFolder, StoreFilename);

    private static ProfileLastMagPresets _profileLastMagPresets = [];

    private static string ProfileId => field ??= ClientAppUtils.GetMainApp().Session.Profile.ProfileId;

    public static MagPreset GetMagPreset(string caliber)
    {
        if (!_profileLastMagPresets.TryGetValue(ProfileId, out var caliberLastPreset)) return null;
        if (!caliberLastPreset.TryGetValue(caliber, out var presetId)) return null;

        var magBuildsStorage = ClientAppUtils.GetClientApp().Session.MagBuildsStorage;
        MagPreset preset = null;
        return magBuildsStorage?.TryFindPresetById(new MongoID(presetId), out preset) == true ? preset : null;
    }

    public static void UpdateMagPreset(MagPreset preset)
    {
        if (!_profileLastMagPresets.TryGetValue(ProfileId, out var caliberLastPreset))
        {
            caliberLastPreset = [];
            _profileLastMagPresets[ProfileId] = caliberLastPreset;
        }

        var caliber = preset.GetCaliberReally();
        var presetId = preset.Id.ToString();
        if (caliberLastPreset.TryGetValue(caliber, out var prevPresetId) && prevPresetId == presetId)
        {
            return;
        }

        caliberLastPreset[caliber] = presetId;
        SaveProfileLastPresets();
    }

    public static void LoadProfileLastPresets()
    {
        if (!File.Exists(_pathToStoreFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_pathToStoreFile);

            _profileLastMagPresets = JsonSerializer.Deserialize<ProfileLastMagPresets>(json) ?? [];
        }
        catch (Exception ex)
        {
            L.Warning($"Caught an exception while trying to load user last used magazine presets\n{ex}");
        }
    }

    private static void SaveProfileLastPresets()
    {
        try
        {
            var json = JsonSerializer.Serialize(_profileLastMagPresets);
            File.WriteAllText(_pathToStoreFile, json);
        }
        catch (Exception ex)
        {
            L.Warning($"Caught an exception while trying to save user last used magazine presets\n{ex}");
        }
    }
}

/// <summary>
/// Key: ProfileId, Value: Key: Caliber, Value: Mag Preset ID
/// </summary>
public class ProfileLastMagPresets : Dictionary<string, CaliberLastPreset>;

/// <summary>
/// Key: Caliber, Value: Mag Preset ID
/// </summary>
public class CaliberLastPreset : Dictionary<string, string>;
