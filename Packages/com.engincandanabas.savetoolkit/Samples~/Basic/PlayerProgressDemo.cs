
using SaveToolkit;
using UnityEngine;

public class PlayerProgressDemo : MonoBehaviour
{
    private const string ProgressKey = "player_progress";

    private async void Start()
    {
        SaveController.Instance.LoadAll(out _);

        SaveProgress();
        LoadProgress();

        var progress = new PlayerProgress();
        progress.AddCoins(250);

        SaveResult saveResult =
            await SaveController.Instance.SaveAsync(ProgressKey, progress);

        if (!saveResult.Success)
        {
            Debug.LogError(saveResult.Error);
            return;
        }

        SaveResult<PlayerProgress> loadResult =
            await SaveController.Instance.LoadAsync<PlayerProgress>(ProgressKey);

        if (loadResult.Success)
            Debug.Log($"Async loaded Level: {loadResult.Data.Level}, Coins: {loadResult.Data.Coins}");
        else
            Debug.LogError(loadResult.Error);
    }

    private void SaveProgress()
    {
        var playerProgress = new PlayerProgress();

        playerProgress.AddCoins(100);
        playerProgress.LevelUp();

        bool success = SaveController.Instance.Save(
            ProgressKey,
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
            ProgressKey,
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
