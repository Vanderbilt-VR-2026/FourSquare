using UnityEngine;
using Fusion;

[RequireComponent(typeof(NetworkObject))]
public class EliminationTeleportation : NetworkBehaviour
{
    [Header("Role Spawn Points")]
    [SerializeField] private Transform kingSpawnPoint;
    [SerializeField] private Transform queenSpawnPoint;
    [SerializeField] private Transform jackSpawnPoint;
    [SerializeField] private Transform peasantSpawnPoint;

    private bool eliminationInProgress;

    public void Eliminate(EliminationPlayer player)
    {
        if (!Object.HasStateAuthority ||
            player == null ||
            eliminationInProgress ||
            GameStateManager.Instance == null)
        {
            return;
        }

        eliminationInProgress = true;
        bool eliminated = GameStateManager.Instance.EliminatePlayer(player.NetworkRole);
        if (eliminated)
        {
            TeleportAllPlayers();
        }

        eliminationInProgress = false;
    }

    private void TeleportAllPlayers()
    {
        EliminationPlayer[] players = FindObjectsByType<EliminationPlayer>(FindObjectsSortMode.None);
        foreach (EliminationPlayer player in players)
        {
            player.TeleportTo(GetSpawnPoint(player.NetworkRole));
        }
    }

    private Transform GetSpawnPoint(SquareRole role)
    {
        switch (role)
        {
            case SquareRole.King:
                return kingSpawnPoint;
            case SquareRole.Queen:
                return queenSpawnPoint;
            case SquareRole.Jack:
                return jackSpawnPoint;
            case SquareRole.Peasant:
                return peasantSpawnPoint;
            default:
                return null;
        }
    }
}
