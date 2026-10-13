using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using Fusion;

[RequireComponent(typeof(XROrigin))]
[RequireComponent(typeof(NetworkObject))]
public class EliminationPlayer : NetworkBehaviour
{
    [Header("Player State")]
    [SerializeField] private string playerId;
    [SerializeField] private SquareRole startingRole = SquareRole.Spectator;

    [Header("XR Input")]
    [Tooltip("Assign the XRI Default Input Actions > XRI Left/Right Interaction > Activate action.")]
    [SerializeField] private InputActionReference triggerAction;

    private XROrigin xrOrigin;
    private bool enabledActionHere;
    [Networked]
    public SquareRole NetworkRole { get; private set; }

    public string PlayerId => playerId;
    public SquareRole CurrentRole => NetworkRole;

    public void InitializeNetworkPlayer(SquareRole role, string networkPlayerId)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        startingRole = role;
        playerId = networkPlayerId;
        NetworkRole = role;

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.RegisterPlayer(playerId, NetworkRole);
        }
    }

    private void Awake()
    {
        xrOrigin = GetComponent<XROrigin>();

        if (string.IsNullOrWhiteSpace(playerId))
        {
            playerId = gameObject.name;
        }
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            if (string.IsNullOrWhiteSpace(playerId) && Object.InputAuthority.IsValid)
            {
                playerId = Object.InputAuthority.ToString();
            }

            NetworkRole = startingRole;

            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.RegisterPlayer(playerId, NetworkRole);
                GameStateManager.Instance.OnPlayerRoleChanged += HandleRoleChanged;
            }
        }

        if (triggerAction != null)
        {
            triggerAction.action.performed += HandleTriggerPerformed;
            if (!triggerAction.action.enabled)
            {
                triggerAction.action.Enable();
                enabledActionHere = true;
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPlayerRoleChanged -= HandleRoleChanged;
        }

        if (triggerAction != null)
        {
            triggerAction.action.performed -= HandleTriggerPerformed;
            if (enabledActionHere)
            {
                triggerAction.action.Disable();
                enabledActionHere = false;
            }
        }
    }

    private void HandleTriggerPerformed(InputAction.CallbackContext context)
    {
        if (!Object.HasInputAuthority || NetworkRole == SquareRole.Spectator)
        {
            return;
        }

        RPC_RequestElimination();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestElimination(RpcInfo info = default)
    {
        if (info.Source != Object.InputAuthority || NetworkRole == SquareRole.Spectator)
        {
            return;
        }

        EliminationTeleportation teleportation = FindFirstObjectByType<EliminationTeleportation>();
        if (teleportation != null)
        {
            teleportation.Eliminate(this);
        }
    }

    private void HandleRoleChanged(FourSquarePlayer player, SquareRole oldRole, SquareRole newRole)
    {
        if (player.playerId == playerId)
        {
            NetworkRole = newRole;
        }
    }

    public void TeleportTo(Transform spawnPoint)
    {
        if (spawnPoint == null || xrOrigin == null)
        {
            return;
        }

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        xrOrigin.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }
}
