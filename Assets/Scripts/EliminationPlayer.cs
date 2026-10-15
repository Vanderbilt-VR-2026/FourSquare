using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
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
    private readonly List<InputAction> subscribedTriggerActions = new List<InputAction>();
    private readonly List<InputAction> enabledTriggerActions = new List<InputAction>();
    private bool leftControllerWasPressed;
    private bool rightControllerWasPressed;
    private bool xriActionWasPressed;
    private float nextEliminationRequestTime;
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

        if (!Object.HasInputAuthority)
        {
            return;
        }

        Debug.Log($"EliminationPlayer input authority ready: {name}, actions={subscribedTriggerActions.Count}, role={NetworkRole}.", this);
        SubscribeTriggerAction(triggerAction != null ? triggerAction.action : null);

        if (triggerAction != null && triggerAction.action != null)
        {
            InputActionAsset inputActions = triggerAction.action.actionMap != null
                ? triggerAction.action.actionMap.asset
                : null;

            if (inputActions != null)
            {
                foreach (InputActionMap actionMap in inputActions.actionMaps)
                {
                    foreach (InputAction action in actionMap.actions)
                    {
                        string actionName = action.name.ToLowerInvariant();
                        if (actionName.Contains("activate") ||
                            actionName.Contains("select") ||
                            actionName.Contains("grip") ||
                            actionName.Contains("trigger"))
                        {
                            SubscribeTriggerAction(action);
                        }
                    }
                }

                Debug.Log($"EliminationPlayer subscribed to {subscribedTriggerActions.Count} XRI actions on {name}.", this);
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPlayerRoleChanged -= HandleRoleChanged;
        }

        UnsubscribeTriggerActions();
    }

    private void OnDestroy()
    {
        UnsubscribeTriggerActions();
    }

    private void UnsubscribeTriggerActions()
    {
        foreach (InputAction action in subscribedTriggerActions)
        {
            action.performed -= HandleTriggerPerformed;
        }

        foreach (InputAction action in enabledTriggerActions)
        {
            action.Disable();
        }

        subscribedTriggerActions.Clear();
        enabledTriggerActions.Clear();
    }

    private void Update()
    {
        if (!Object || !Object.HasInputAuthority)
        {
            return;
        }

        bool leftPressed = ReadButton(
            UnityEngine.XR.InputDeviceCharacteristics.Left,
            UnityEngine.XR.CommonUsages.triggerButton) ||
            ReadAxis(
                UnityEngine.XR.InputDeviceCharacteristics.Left,
                UnityEngine.XR.CommonUsages.trigger) > 0.5f ||
            ReadButton(
                UnityEngine.XR.InputDeviceCharacteristics.Left,
                UnityEngine.XR.CommonUsages.gripButton) ||
            ReadAxis(
                UnityEngine.XR.InputDeviceCharacteristics.Left,
                UnityEngine.XR.CommonUsages.grip) > 0.5f;
        bool rightPressed = ReadButton(
            UnityEngine.XR.InputDeviceCharacteristics.Right,
            UnityEngine.XR.CommonUsages.triggerButton) ||
            ReadAxis(
                UnityEngine.XR.InputDeviceCharacteristics.Right,
                UnityEngine.XR.CommonUsages.trigger) > 0.5f ||
            ReadButton(
                UnityEngine.XR.InputDeviceCharacteristics.Right,
                UnityEngine.XR.CommonUsages.gripButton) ||
            ReadAxis(
                UnityEngine.XR.InputDeviceCharacteristics.Right,
                UnityEngine.XR.CommonUsages.grip) > 0.5f;
        bool xriActionPressed = IsXriControllerActionPressed();

        if ((leftPressed && !leftControllerWasPressed) ||
            (rightPressed && !rightControllerWasPressed) ||
            (xriActionPressed && !xriActionWasPressed))
        {
            RequestElimination();
        }

        leftControllerWasPressed = leftPressed;
        rightControllerWasPressed = rightPressed;
        xriActionWasPressed = xriActionPressed;
    }

    private static bool ReadButton(
        UnityEngine.XR.InputDeviceCharacteristics hand,
        UnityEngine.XR.InputFeatureUsage<bool> button)
    {
        List<UnityEngine.XR.InputDevice> devices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.HeldInHand |
            UnityEngine.XR.InputDeviceCharacteristics.Controller |
            hand,
            devices);

        foreach (UnityEngine.XR.InputDevice device in devices)
        {
            if (device.TryGetFeatureValue(button, out bool pressed) &&
                pressed)
            {
                return true;
            }
        }

        return false;
    }

    private static float ReadAxis(
        UnityEngine.XR.InputDeviceCharacteristics hand,
        UnityEngine.XR.InputFeatureUsage<float> axis)
    {
        List<UnityEngine.XR.InputDevice> devices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.HeldInHand |
            UnityEngine.XR.InputDeviceCharacteristics.Controller |
            hand,
            devices);

        foreach (UnityEngine.XR.InputDevice device in devices)
        {
            if (device.TryGetFeatureValue(axis, out float value))
            {
                return value;
            }
        }

        return 0f;
    }

    private bool IsXriControllerActionPressed()
    {
        foreach (InputAction action in subscribedTriggerActions)
        {
            if (action.IsPressed())
            {
                return true;
            }
        }

        return false;
    }

    private void SubscribeTriggerAction(InputAction action)
    {
        if (action == null || subscribedTriggerActions.Contains(action))
        {
            return;
        }

        action.performed += HandleTriggerPerformed;
        if (!action.enabled)
        {
            action.Enable();
            enabledTriggerActions.Add(action);
        }

        subscribedTriggerActions.Add(action);
    }

    private void HandleTriggerPerformed(InputAction.CallbackContext context)
    {
        if (!isActiveAndEnabled || Object == null || !Object.IsValid || !Object.HasInputAuthority)
        {
            return;
        }

        RequestElimination();
    }

    private void RequestElimination()
    {
        if (Object == null
            || !Object.IsValid
            || !Object.HasInputAuthority
            || NetworkRole == SquareRole.Spectator
            || !System.Enum.IsDefined(typeof(SquareRole), NetworkRole))
        {
            return;
        }

        if (Time.unscaledTime < nextEliminationRequestTime)
        {
            return;
        }

        nextEliminationRequestTime = Time.unscaledTime + 0.5f;
        Debug.Log($"Elimination input detected for {name}; stateAuthority={Object.HasStateAuthority}, role={NetworkRole}.", this);

        if (Object.HasStateAuthority)
        {
            EliminateOnStateAuthority();
        }
        else
        {
            RPC_RequestElimination();
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestElimination(RpcInfo info = default)
    {
        Debug.Log($"Elimination RPC received for {name} from {info.Source}.", this);

        if (info.Source != Object.InputAuthority)
        {
            Debug.LogWarning($"Ignoring elimination RPC for {name}: source does not match input authority.", this);
            return;
        }

        EliminateOnStateAuthority();
    }

    private void EliminateOnStateAuthority()
    {
        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning($"Cannot eliminate {name}: this instance is not state authority.", this);
            return;
        }

        EliminationTeleportation teleportation = FindFirstObjectByType<EliminationTeleportation>();
        if (teleportation == null)
        {
            Debug.LogError($"No EliminationTeleportation was found for {name}.", this);
            return;
        }

        Debug.Log($"Eliminating {name} from role {NetworkRole}.", teleportation);
        teleportation.Eliminate(this);
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

    public void TeleportNetworkPlayer(Transform spawnPoint)
    {
        if (spawnPoint == null)
        {
            Debug.LogError($"No spawn point was configured for {name}, role={NetworkRole}.", this);
            return;
        }

        if (Object.HasInputAuthority)
        {
            Debug.Log($"Teleporting local player {name} to {spawnPoint.name}.", this);
            TeleportTo(spawnPoint.position, spawnPoint.rotation);
            return;
        }

        if (Object.HasStateAuthority)
        {
            Debug.Log($"Sending teleport RPC for {name} to {spawnPoint.name}.", this);
            RPC_TeleportTo(spawnPoint.position, spawnPoint.rotation);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_TeleportTo(Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        Debug.Log($"Teleport RPC received for {name}.", this);
        TeleportTo(position, rotation);
    }

    private void TeleportTo(Vector3 position, Quaternion rotation)
    {
        if (xrOrigin == null)
        {
            return;
        }

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        xrOrigin.transform.SetPositionAndRotation(position, rotation);

        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }
}
