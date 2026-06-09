using System;
using System.Collections;
using UnityEngine;

public enum ObjectHeadCharacterKind
{
    Bulb,
    Seed,
    Bomb
}

[DisallowMultipleComponent]
public class CharacterVisual : MonoBehaviour
{
    private const float TemporaryCommonHeadScaleMultiplier = 0.66f;

    [SerializeField] private ObjectHeadCharacterKind characterKind = ObjectHeadCharacterKind.Bulb;
    [SerializeField, Range(0, 2)] private int selectedSkillIndex;
    [SerializeField] private bool spriteFacesRightByDefault;
    [SerializeField] private bool hideRootSpriteRenderer = true;
    [SerializeField] private Vector2 bodyLocalOffset = new Vector2(0f, -0.24f);
    [SerializeField] private Vector2 headLocalOffset = new Vector2(0f, 0.34f);
    [SerializeField, Min(0.01f)] private float bodyScale = 1f;
    [SerializeField, Min(0.01f)] private float headScale = 1f;
    [SerializeField] private int bodySortingOrder = 10;
    [SerializeField] private int headSortingOrder = 11;

    private SpriteRenderer bodyRenderer;
    private SpriteRenderer headRenderer;
    private Sprite bodyIdle;
    private Sprite bodyThrow;
    private Sprite bodyHit;
    private Sprite[,] headSprites;
    private Sprite uniqueHeadSprite;
    private Sprite temporaryCommonHeadSprite;
    private Coroutine temporaryStateRoutine;
    private bool isDead;
    private bool isHeadHidden;
    private bool isFacingRight = true;

    public ObjectHeadCharacterKind CharacterKind => characterKind;
    public int SelectedSkillIndex => selectedSkillIndex;
    public Sprite CurrentHeadSprite => headRenderer != null ? headRenderer.sprite : null;
    public Sprite UniqueHeadSprite => uniqueHeadSprite;
    public bool IsFacingRight => isFacingRight;
    public bool SpriteFacesRightByDefault => spriteFacesRightByDefault;
    public event Action<bool> FacingChanged;

    private void Awake()
    {
        LoadSprites();
        BuildRenderers();
        ApplyVisualState();
    }

    private void OnValidate()
    {
        bodyScale = Mathf.Max(0.01f, bodyScale);
        headScale = Mathf.Max(0.01f, headScale);
        selectedSkillIndex = Mathf.Clamp(selectedSkillIndex, 0, 2);
        if (bodyRenderer != null || headRenderer != null)
        {
            ApplyVisualState();
        }
    }

    public void SetCharacterKind(ObjectHeadCharacterKind kind)
    {
        characterKind = kind;
        selectedSkillIndex = Mathf.Clamp(selectedSkillIndex, 0, 2);
        uniqueHeadSprite = GetSelectedHeadSprite();
        ApplyVisualState();
    }

    public void SetSkillIndex(int index)
    {
        selectedSkillIndex = Mathf.Clamp(index, 0, 2);
        uniqueHeadSprite = GetSelectedHeadSprite();
        ApplyVisualState();
    }

    public void ConfigureSpriteFacing(bool facesRightByDefault)
    {
        spriteFacesRightByDefault = facesRightByDefault;
        ApplyFacing();
    }

    public void SetUniqueHead(Sprite sprite)
    {
        uniqueHeadSprite = sprite != null ? sprite : GetSelectedHeadSprite();
        if (temporaryCommonHeadSprite == null)
        {
            RefreshHeadSprite();
        }
    }

    public void SetTemporaryCommonHead(Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        temporaryCommonHeadSprite = sprite;
        RefreshHeadSprite();
    }

    public void RestoreUniqueHead()
    {
        temporaryCommonHeadSprite = null;
        RefreshHeadSprite();
        ShowHeadAfterAction();
    }

    public void HideHeadForThrow()
    {
        SetHeadVisible(false);
    }

    public void ShowHeadAfterAction()
    {
        SetHeadVisible(true);
    }

    public void RestoreAfterChargeCancel()
    {
        if (temporaryStateRoutine != null)
        {
            StopCoroutine(temporaryStateRoutine);
            temporaryStateRoutine = null;
        }

        if (!isDead)
        {
            SetBodySprite(bodyIdle);
            SetHeadVisible(true);
            SetRendererColor(Color.white);
            RefreshHeadSprite();
        }
    }

    public void PlayThrowPose(float seconds)
    {
        if (temporaryStateRoutine != null)
        {
            StopCoroutine(temporaryStateRoutine);
        }

        temporaryStateRoutine = StartCoroutine(TemporaryBodyState(bodyThrow, false, Color.white, seconds));
    }

    public void PlayHitFlash(Color flashColor, float seconds)
    {
        if (isDead)
        {
            return;
        }

        if (temporaryStateRoutine != null)
        {
            StopCoroutine(temporaryStateRoutine);
        }

        temporaryStateRoutine = StartCoroutine(TemporaryBodyState(bodyHit, true, flashColor, seconds));
    }

    public void SetDead(Color deadColor)
    {
        isDead = true;
        SetBodySprite(bodyHit);
        SetHeadVisible(true);
        SetRendererColor(deadColor);
    }

    public void SetFacingRight(bool facingRight)
    {
        bool changed = isFacingRight != facingRight;
        isFacingRight = facingRight;
        ApplyFacing();
        if (changed)
        {
            FacingChanged?.Invoke(isFacingRight);
        }
    }

    private IEnumerator TemporaryBodyState(Sprite bodySprite, bool showHead, Color color, float seconds)
    {
        SetBodySprite(bodySprite);
        SetHeadVisible(showHead);
        SetRendererColor(color);
        yield return new WaitForSeconds(Mathf.Max(0.01f, seconds));

        if (!isDead)
        {
            SetBodySprite(bodyIdle);
            SetHeadVisible(true);
            SetRendererColor(Color.white);
        }

        temporaryStateRoutine = null;
    }

    private void BuildRenderers()
    {
        if (hideRootSpriteRenderer)
        {
            SpriteRenderer rootRenderer = GetComponent<SpriteRenderer>();
            if (rootRenderer != null)
            {
                rootRenderer.enabled = false;
            }
        }

        bodyRenderer = GetOrCreateChildRenderer("BodyRenderer", bodySortingOrder);
        headRenderer = GetOrCreateChildRenderer("HeadRenderer", headSortingOrder);
        uniqueHeadSprite = GetSelectedHeadSprite();
        ApplyFacing();
    }

    private SpriteRenderer GetOrCreateChildRenderer(string childName, int sortingOrder)
    {
        Transform child = transform.Find(childName);
        if (child == null)
        {
            child = new GameObject(childName).transform;
            child.SetParent(transform, false);
        }

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = child.gameObject.AddComponent<SpriteRenderer>();
        }

        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void LoadSprites()
    {
        bodyIdle = Resources.Load<Sprite>("Sprites/Body/body_idle");
        bodyThrow = Resources.Load<Sprite>("Sprites/Body/body_throw");
        bodyHit = Resources.Load<Sprite>("Sprites/Body/body_hit");

        headSprites = new Sprite[3, 3];
        headSprites[(int)ObjectHeadCharacterKind.Bulb, 0] = Resources.Load<Sprite>("Sprites/Heads/head_bulb_broken_off");
        headSprites[(int)ObjectHeadCharacterKind.Bulb, 1] = Resources.Load<Sprite>("Sprites/Heads/head_bulb_on");
        headSprites[(int)ObjectHeadCharacterKind.Bulb, 2] = Resources.Load<Sprite>("Sprites/Heads/head_bulb_off");
        headSprites[(int)ObjectHeadCharacterKind.Seed, 0] = Resources.Load<Sprite>("Sprites/Heads/head_seed");
        headSprites[(int)ObjectHeadCharacterKind.Seed, 1] = Resources.Load<Sprite>("Sprites/Heads/head_vine_ball");
        headSprites[(int)ObjectHeadCharacterKind.Seed, 2] = Resources.Load<Sprite>("Sprites/Heads/head_thorn_vine_ball");
        headSprites[(int)ObjectHeadCharacterKind.Bomb, 0] = Resources.Load<Sprite>("Sprites/Heads/head_bomb_green");
        headSprites[(int)ObjectHeadCharacterKind.Bomb, 1] = Resources.Load<Sprite>("Sprites/Heads/head_bomb_red");
        headSprites[(int)ObjectHeadCharacterKind.Bomb, 2] = Resources.Load<Sprite>("Sprites/Heads/head_bomb_yellow");
    }

    private void ApplyVisualState()
    {
        if (bodyRenderer == null || headRenderer == null)
        {
            return;
        }

        SetBodySprite(isDead ? bodyHit : bodyIdle);
        if (uniqueHeadSprite == null)
        {
            uniqueHeadSprite = GetSelectedHeadSprite();
        }
        RefreshHeadSprite();
        bodyRenderer.transform.localPosition = bodyLocalOffset;
        headRenderer.transform.localPosition = headLocalOffset;
        bodyRenderer.transform.localScale = Vector3.one * bodyScale;
        ApplyHeadScale();
        bodyRenderer.sortingOrder = bodySortingOrder;
        headRenderer.sortingOrder = headSortingOrder;
        ApplyFacing();
        SetHeadVisible(!isHeadHidden);
    }

    private void ApplyFacing()
    {
        bool flip = isFacingRight != spriteFacesRightByDefault;
        if (bodyRenderer != null)
        {
            bodyRenderer.flipX = flip;
        }

        if (headRenderer != null)
        {
            headRenderer.flipX = flip;
        }
    }

    private Sprite GetSelectedHeadSprite()
    {
        return headSprites != null
            ? headSprites[(int)characterKind, Mathf.Clamp(selectedSkillIndex, 0, 2)]
            : null;
    }

    private void RefreshHeadSprite()
    {
        if (headRenderer == null)
        {
            return;
        }

        headRenderer.sprite = temporaryCommonHeadSprite != null
            ? temporaryCommonHeadSprite
            : uniqueHeadSprite != null
                ? uniqueHeadSprite
                : GetSelectedHeadSprite();
        ApplyHeadScale();
        headRenderer.enabled = !isHeadHidden && headRenderer.sprite != null;
        ApplyFacing();
    }

    private void ApplyHeadScale()
    {
        if (headRenderer == null)
        {
            return;
        }

        float scaleMultiplier = temporaryCommonHeadSprite != null
            ? TemporaryCommonHeadScaleMultiplier
            : 1f;
        headRenderer.transform.localScale = Vector3.one * headScale * scaleMultiplier;
    }

    private void SetBodySprite(Sprite sprite)
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.sprite = sprite;
        }
    }

    private void SetHeadVisible(bool visible)
    {
        isHeadHidden = !visible;
        if (headRenderer != null)
        {
            headRenderer.enabled = visible && headRenderer.sprite != null;
        }
    }

    private void SetRendererColor(Color color)
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.color = color;
        }

        if (headRenderer != null)
        {
            headRenderer.color = color;
        }
    }
}
