namespace CarGame.Services;

// simple abstraction over preferences so viewmodels do not talk to Preferences directly
public interface IProfileService
{
    // stored currency the player can spend in the shop
    int CoinsHeld { get; set; }

    // lifetime coins earned (useful for stats)
    int CoinsEarnedTotal { get; set; }

    // best score across all runs
    int HighScore { get; set; }

    // sprite filename for the selected car
    string SelectedCarSprite { get; set; }

    // file path for a user picked custom car image (optional)
    string CustomCarPath { get; set; }

    // ownership flags for unlockable cars
    bool OwnedPurple { get; set; }
    bool OwnedBlue { get; set; }
    bool OwnedGreen { get; set; }

    // upgrades applied to the game engine
    int MaxHealth { get; set; }               // 3..6
    int InvincibilitySeconds { get; set; }    // 6..12

    // audio settings for music and sfx
    double MasterVolume { get; set; }         // 0..1
    bool SfxEnabled { get; set; }

    // wipes all saved values back to defaults
    void EraseAll();
}
