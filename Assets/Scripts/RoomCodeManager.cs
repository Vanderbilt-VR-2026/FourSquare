using System.Collections.Generic;
using UnityEngine;

public class RoomCodeManager : MonoBehaviour
{
    private const int CodeLength = 4;
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Temporary local storage.
    // Later this should be replaced by the multiplayer/backend room registry.
    private readonly HashSet<string> usedRoomCodes = new HashSet<string>();

    public string CreateUniqueRoomCode()
    {
        string roomCode;

        do
        {
            roomCode = GenerateRoomCode();
        }
        while (usedRoomCodes.Contains(roomCode));

        usedRoomCodes.Add(roomCode);

        Debug.Log("Created room code: " + roomCode);

        return roomCode;
    }

    private string GenerateRoomCode()
    {
        char[] code = new char[CodeLength];

        for (int i = 0; i < CodeLength; i++)
        {
            int randomIndex = Random.Range(0, Characters.Length);
            code[i] = Characters[randomIndex];
        }

        return new string(code);
    }

    public bool IsRoomCodeUsed(string roomCode)
    {
        return usedRoomCodes.Contains(roomCode);
    }
}