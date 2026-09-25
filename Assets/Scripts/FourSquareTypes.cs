// File: FourSquareTypes.cs
using UnityEngine;

public enum SquareRole
{
    King = 4,
    Queen = 3,
    Jack = 2,
    Peasant = 1,
    Spectator = 0
}

[System.Serializable] // Makes it visible in the Unity Inspector when used on scripts
public class FourSquarePlayer
{
    public string playerId;
    public string playerName;
    public SquareRole currentRole;
    public int score;
    public int eliminationsCount;

    public FourSquarePlayer(string id, string name, SquareRole initialRole)
    {
        playerId = id;
        playerName = name;
        currentRole = initialRole;
        score = 0;
        eliminationsCount = 0;
    }
}