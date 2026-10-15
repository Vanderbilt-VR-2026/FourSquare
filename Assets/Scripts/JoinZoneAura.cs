using UnityEngine;

[DisallowMultipleComponent]
public sealed class JoinZoneAura : MonoBehaviour
{
    [SerializeField] private Renderer auraRenderer;
    [SerializeField, Min(1)] private int maximumPlayers = 4;
    [SerializeField, Range(0f, 1f)] private float transparency = 0.3f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock propertyBlock;
    private bool isClosed;
    private bool missingRendererWarningLogged;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        if (auraRenderer == null) auraRenderer = GetComponent<Renderer>();
        if (auraRenderer == null) auraRenderer = GetComponentInChildren<Renderer>(true);
    }

    public void Configure(Renderer targetRenderer, int playerLimit)
    {
        if (targetRenderer != null) auraRenderer = targetRenderer;
        maximumPlayers = Mathf.Max(1, playerLimit);
        RefreshState();
    }

    public void SetClosed(bool closed)
    {
        isClosed = closed;
        RefreshState();
    }

    public void RefreshState()
    {
        if (propertyBlock == null) return;
        if (auraRenderer == null)
        {
            if (!missingRendererWarningLogged)
            {
                Debug.LogError("Join Zone Aura has no Renderer assigned or available on this object/children.", this);
                missingRendererWarningLogged = true;
            }
            return;
        }

        int playersInCourt = PlayerJoinCourt.ConnectedPlayerCount;
        Color stateColor = playersInCourt == 0 ? Color.blue : Color.white;
        bool gameStarted = PlayerJoinCourt.IsGameInProgress;
        if (isClosed || (gameStarted && playersInCourt >= maximumPlayers))
        {
            stateColor = Color.red;
        }
        else if (gameStarted && playersInCourt < maximumPlayers)
        {
            stateColor = Color.blue;
        }
        stateColor.a = transparency;

        auraRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, stateColor);
        propertyBlock.SetColor(ColorId, stateColor);
        propertyBlock.SetColor(EmissionColorId, stateColor);
        auraRenderer.SetPropertyBlock(propertyBlock);
    }
}
