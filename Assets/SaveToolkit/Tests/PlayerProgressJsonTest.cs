
using UnityEngine;

public class PlayerProgressJsonTest : MonoBehaviour
{
    private void Start()
    {
        SaveProgress();
        LoadProgress();
    }

    private void SaveProgress()
    {
        var playerProgress = new PlayerProgress();

        playerProgress.AddCoins(100);
        playerProgress.LevelUp();

        bool success = SaveController.Instance.Save(
            "player_progress",
            playerProgress,
            out string error
        );

        if (success)
        {
            Debug.Log("Player progress saved successfully.");
        }
        else
        {
            Debug.LogError(error);
        }
    }

    private void LoadProgress()
    {
        bool success = SaveController.Instance.TryLoad<PlayerProgress>(
            "player_progress",
            out var loadedProgress,
            out string error
        );

        if (success)
        {
            Debug.Log($"Loaded Level: {loadedProgress.Level}");
            Debug.Log($"Loaded Coins: {loadedProgress.Coins}");
        }
        else
        {
            Debug.LogError(error);
        }
    }
}
