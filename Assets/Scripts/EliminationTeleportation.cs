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
        if (player == null ||
            player.Object == null ||
            !player.Object.IsValid ||
            !player.Object.HasStateAuthority ||
            player.NetworkRole == SquareRole.Spectator ||
            !System.Enum.IsDefined(typeof(SquareRole), player.NetworkRole) ||
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
            if (player == null
                || !System.Enum.IsDefined(typeof(SquareRole), player.NetworkRole)
                || player.NetworkRole == SquareRole.Spectator)
            {
                continue;
            }

            Transform spawnPoint = GetSpawnPoint(player.NetworkRole);
            if (spawnPoint == null)
            {
                Debug.LogError($"No spawn point is configured for the {player.NetworkRole} role.", this);
                continue;
            }

            player.TeleportNetworkPlayer(spawnPoint);
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
