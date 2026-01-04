namespace CarGame.Services;

public interface IProfileService
{
    int CoinsHeld { get; set; }
    int CoinsEarnedTotal { get; set; }

    int HighScore { get; set; }

    string SelectedCarSprite { get; set; }
    string CustomCarPath { get; set; }

    bool OwnedPurple { get; set; }
    bool OwnedBlue { get; set; }
    bool OwnedGreen { get; set; }

    int MaxHealth { get; set; }               // 3..6
    int InvincibilitySeconds { get; set; }    // 6..12

    double MasterVolume { get; set; }         // 0..1
    bool SfxEnabled { get; set; }

    void EraseAll();
}
