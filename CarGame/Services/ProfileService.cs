using Microsoft.Maui.Storage;

namespace CarGame.Services;

public sealed class ProfileService : IProfileService
{
    private const string PrefSelectedCar = "selected_car_sprite";
    private const string PrefCustomCarPath = "custom_car_path";
    private const string PrefHighScore = "highscore";

    private const string PrefCoinsHeld = "coins_held";
    private const string PrefCoinsEarnedTotal = "coins_earned_total";
    private const string PrefTotalCoinsLegacy = "total_coins";

    private const string PrefOwnedPurple = "owned_purple";
    private const string PrefOwnedBlue = "owned_blue";
    private const string PrefOwnedGreen = "owned_green";

    private const string PrefMaxHealth = "max_health";
    private const string PrefInvincibilityDurationSeconds = "invincibility_duration_seconds";

    private const string PrefMasterVolume = "master_volume"; // 0..1
    private const string PrefSfxEnabled = "sfx_enabled";

    public ProfileService()
    {
        // One-time migration for older builds: if legacy total coins exists but new fields are 0, seed them.
        var legacyTotal = Preferences.Default.Get(PrefTotalCoinsLegacy, 0);
        var held = Preferences.Default.Get(PrefCoinsHeld, 0);
        var earned = Preferences.Default.Get(PrefCoinsEarnedTotal, 0);

        if (legacyTotal > 0 && held == 0 && earned == 0)
        {
            Preferences.Default.Set(PrefCoinsHeld, legacyTotal);
            Preferences.Default.Set(PrefCoinsEarnedTotal, legacyTotal);
        }
    }

    public int CoinsHeld
    {
        get => Preferences.Default.Get(PrefCoinsHeld, 0);
        set => Preferences.Default.Set(PrefCoinsHeld, Math.Max(0, value));
    }

    public int CoinsEarnedTotal
    {
        get => Preferences.Default.Get(PrefCoinsEarnedTotal, 0);
        set => Preferences.Default.Set(PrefCoinsEarnedTotal, Math.Max(0, value));
    }

    public int HighScore
    {
        get => Preferences.Default.Get(PrefHighScore, 0);
        set => Preferences.Default.Set(PrefHighScore, Math.Max(0, value));
    }

    public string SelectedCarSprite
    {
        get => Preferences.Default.Get(PrefSelectedCar, "yellowcar.png");
        set => Preferences.Default.Set(PrefSelectedCar, value ?? "yellowcar.png");
    }

    public string CustomCarPath
    {
        get => Preferences.Default.Get(PrefCustomCarPath, string.Empty);
        set => Preferences.Default.Set(PrefCustomCarPath, value ?? string.Empty);
    }

    public bool OwnedPurple
    {
        get => Preferences.Default.Get(PrefOwnedPurple, false);
        set => Preferences.Default.Set(PrefOwnedPurple, value);
    }

    public bool OwnedBlue
    {
        get => Preferences.Default.Get(PrefOwnedBlue, false);
        set => Preferences.Default.Set(PrefOwnedBlue, value);
    }

    public bool OwnedGreen
    {
        get => Preferences.Default.Get(PrefOwnedGreen, false);
        set => Preferences.Default.Set(PrefOwnedGreen, value);
    }

    public int MaxHealth
    {
        get => Math.Clamp(Preferences.Default.Get(PrefMaxHealth, 3), 3, 6);
        set => Preferences.Default.Set(PrefMaxHealth, Math.Clamp(value, 3, 6));
    }

    public int InvincibilitySeconds
    {
        get => Math.Clamp(Preferences.Default.Get(PrefInvincibilityDurationSeconds, 6), 6, 12);
        set => Preferences.Default.Set(PrefInvincibilityDurationSeconds, Math.Clamp(value, 6, 12));
    }

    public double MasterVolume
    {
        get => Math.Clamp(Preferences.Default.Get(PrefMasterVolume, 0.5), 0.0, 1.0);
        set => Preferences.Default.Set(PrefMasterVolume, Math.Clamp(value, 0.0, 1.0));
    }

    public bool SfxEnabled
    {
        get => Preferences.Default.Get(PrefSfxEnabled, true);
        set => Preferences.Default.Set(PrefSfxEnabled, value);
    }

    public void EraseAll()
    {
        Preferences.Default.Remove(PrefHighScore);
        Preferences.Default.Remove(PrefCoinsHeld);
        Preferences.Default.Remove(PrefCoinsEarnedTotal);
        Preferences.Default.Remove(PrefTotalCoinsLegacy);

        Preferences.Default.Remove(PrefOwnedPurple);
        Preferences.Default.Remove(PrefOwnedBlue);
        Preferences.Default.Remove(PrefOwnedGreen);

        Preferences.Default.Remove(PrefSelectedCar);
        Preferences.Default.Remove(PrefCustomCarPath);

        Preferences.Default.Remove(PrefMaxHealth);
        Preferences.Default.Remove(PrefInvincibilityDurationSeconds);

        Preferences.Default.Remove(PrefMasterVolume);
        Preferences.Default.Remove(PrefSfxEnabled);
    }
}
