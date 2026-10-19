#if SAVETOOLKIT_MESSAGEPACK
using MessagePack;
#endif

// [MessagePackObject] lets the MessagePack source generator create an
// AOT-safe formatter, required for MessagePack profiles on IL2CPP (mobile).
// "partial" lets the generator access the private setters. The attributes
// compile only when MessagePack is installed; JSON needs none of them.
#if SAVETOOLKIT_MESSAGEPACK
[MessagePackObject(true)]
#endif
public partial class PlayerProgress
{
    public PlayerProgress() { }

#if SAVETOOLKIT_MESSAGEPACK
    [SerializationConstructor]
#endif
    public PlayerProgress(int level, int coins)
    {
        Level = level;
        Coins = coins;
    }

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
