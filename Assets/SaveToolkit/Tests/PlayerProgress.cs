using UnityEngine;

public class PlayerProgress
{
    public int Level { get; private set; } = 1;
    public int Coins { get; private set; } = 0;

    public void AddCoins(int amount)
    {
        Coins += amount;
    }
    public void LevelUp()
    {
        Level++;
    }
    public void ResetProgress()
    {
        Level = 1;
        Coins = 0;
    }
}
