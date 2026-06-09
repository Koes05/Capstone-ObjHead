using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class CharacterCombat : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHp = 100;
    [SerializeField, Min(0.1f)] private float throwPower = 1f;
    [SerializeField, Min(0.1f)] private float knockbackResistance = 1f;
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color deadColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [SerializeField, Min(0.01f)] private float hitFlashSeconds = 0.15f;
    [SerializeField, Min(0f)] private float knockbackControlLockSeconds = 0.9f;

    [Header("Health Label")]
    [SerializeField] private bool showHealthLabel = true;
    [SerializeField] private Vector2 healthLabelOffset = new Vector2(0f, 0.36f);
    [SerializeField, Min(0.01f)] private float healthLabelCharacterSize = 0.11f;
    [SerializeField, Min(0f)] private float healthLabelAnimationSeconds = 0.3f;
    [SerializeField] private Color healthyLabelColor = Color.white;
    [SerializeField] private Color warningLabelColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color dangerLabelColor = new Color(1f, 0.28f, 0.18f, 1f);
    [SerializeField] private Color deadLabelColor = new Color(0.65f, 0.65f, 0.65f, 1f);
    [SerializeField] private Color healthLabelShadowColor = new Color(0f, 0f, 0f, 0.8f);
    [SerializeField] private int healthLabelSortingOrder = 70;

    [Header("Damage Popup")]
    [SerializeField] private Vector2 damagePopupOffset = new Vector2(0f, 0.54f);
    [SerializeField, Min(0.01f)] private float damagePopupCharacterSizeMultiplier = 0.75f;
    [SerializeField, Min(0.01f)] private float damagePopupSeconds = 0.7f;
    [SerializeField, Min(0f)] private float damagePopupRiseDistance = 1.68f;
    [SerializeField] private Color damagePopupColor = new Color(1f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color damagePopupShadowColor = new Color(0f, 0f, 0f, 0.85f);

    private Rigidbody2D body;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    private TurnCharacterController turnController;
    private CharacterVisual characterVisual;
    private TurnManager turnManager;
    private Color originalColor = Color.white;
    private Coroutine hitFlashRoutine;
    private Coroutine healthLabelAnimationRoutine;
    private int currentHp;
    private int displayedHp;
    private int pendingDamage;
    private bool isDead;
    private Transform healthLabelRoot;
    private TextMesh healthLabelText;
    private TextMesh healthLabelShadow;
    private string lastHealthLabelText;
    private Coroutine damagePopupRoutine;
    private Transform damagePopupRoot;
    private TextMesh damagePopupText;
    private TextMesh damagePopupShadow;

    public event Action<CharacterCombat> Died;

    public int CurrentHp => currentHp;
    public int MaxHp => maxHp;
    public int PendingDamage => pendingDamage;
    public float ThrowPower => throwPower;
    public float KnockbackResistance => knockbackResistance;
    public bool IsDead => isDead;
    public bool ShouldIgnoreFallDamage =>
        turnController != null && turnController.IgnoreFallDamageUntilGrounded;
    public Vector2 KnockbackCenter => bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        turnController = GetComponent<TurnCharacterController>();
        bodyCollider = GetComponent<Collider2D>();
        characterVisual = GetComponent<CharacterVisual>();
        turnManager = FindTurnManager();
        currentHp = maxHp;
        displayedHp = currentHp;

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        EnsureHealthLabel();
        RefreshHealthLabel();
    }

    private void LateUpdate()
    {
        UpdateHealthLabelPosition();
    }

    public void ConfigureStats(int hp, float throwPowerMultiplier, float knockbackResistanceValue)
    {
        maxHp = Mathf.Max(1, hp);
        throwPower = Mathf.Max(0.1f, throwPowerMultiplier);
        knockbackResistance = Mathf.Max(0.1f, knockbackResistanceValue);
        currentHp = maxHp;
        displayedHp = currentHp;
        pendingDamage = 0;
        isDead = false;
        StopHealthLabelAnimation();
        StopDamagePopup();
        RefreshHealthLabel();

        if (bodyCollider != null)
        {
            bodyCollider.enabled = true;
        }

        if (body != null)
        {
            body.simulated = true;
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead || damage <= 0)
        {
            return;
        }

        if (ShouldDeferDamage())
        {
            pendingDamage += damage;
            FlashHit();
            Debug.Log($"{name} queued {damage} damage until residual time ends. Pending: {pendingDamage}.");
            return;
        }

        ApplyDamageNow(damage);
    }

    public int ApplyPendingDamage()
    {
        if (isDead || pendingDamage <= 0)
        {
            pendingDamage = 0;
            return 0;
        }

        int damage = pendingDamage;
        pendingDamage = 0;
        ShowDamagePopup(damage);
        ApplyDamageNow(damage);
        return damage;
    }

    private void ApplyDamageNow(int damage)
    {
        currentHp = Mathf.Max(0, currentHp - damage);
        AnimateHealthLabelTo(currentHp);
        Debug.Log($"{name} HP: {currentHp}/{maxHp}");

        if (currentHp <= 0)
        {
            Die(false);
            return;
        }

        FlashHit();
    }

    public void ApplyKnockback(Vector2 force)
    {
        if (isDead || body == null)
        {
            return;
        }

        Vector2 velocity = body.linearVelocity;
        if (velocity.y < 0f)
        {
            velocity.y = 0f;
            body.linearVelocity = velocity;
        }

        turnController?.PreserveExternalMotion(knockbackControlLockSeconds);
        body.AddForce(force / knockbackResistance, ForceMode2D.Impulse);
    }

    public void Die()
    {
        Die(true);
    }

    private void Die(bool updateHealthLabelImmediately)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        currentHp = 0;
        pendingDamage = 0;
        if (updateHealthLabelImmediately)
        {
            StopHealthLabelAnimation();
            displayedHp = 0;
            RefreshHealthLabel();
        }
        turnController?.ClearFallDamageImmunity();
        turnController?.SetControlEnabled(false);
        turnController?.StopHorizontalMovement();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
        }

        if (bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }

        if (characterVisual != null)
        {
            characterVisual.SetDead(deadColor);
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.color = deadColor;
        }

        Debug.Log($"{name} died.");
        Died?.Invoke(this);
    }

    private bool ShouldDeferDamage()
    {
        if (turnManager == null)
        {
            turnManager = FindTurnManager();
        }

        return turnManager != null && turnManager.IsResidualTimeActive;
    }

    private void EnsureHealthLabel()
    {
        if (!showHealthLabel)
        {
            return;
        }

        Transform existingRoot = transform.Find("HealthLabel");
        if (existingRoot != null && existingRoot.GetComponent<Canvas>() != null)
        {
            Destroy(existingRoot.gameObject);
            existingRoot = null;
        }

        healthLabelRoot = existingRoot != null
            ? existingRoot
            : new GameObject("HealthLabel").transform;
        healthLabelRoot.SetParent(transform, false);
        healthLabelRoot.localRotation = Quaternion.identity;
        healthLabelRoot.localScale = Vector3.one;

        healthLabelShadow = GetOrCreateHealthText("HealthLabelShadow", healthLabelShadowColor, healthLabelSortingOrder);
        healthLabelShadow.transform.SetParent(healthLabelRoot, false);
        healthLabelShadow.transform.localPosition = new Vector3(0.035f, -0.035f, 0f);

        healthLabelText = GetOrCreateHealthText("HealthLabelText", healthyLabelColor, healthLabelSortingOrder + 1);
        healthLabelText.transform.SetParent(healthLabelRoot, false);
        healthLabelText.transform.localPosition = Vector3.zero;
    }

    private TextMesh GetOrCreateHealthText(string objectName, Color color, int sortingOrder)
    {
        Transform child = healthLabelRoot.Find(objectName);
        if (child == null)
        {
            child = new GameObject(objectName).transform;
        }

        TextMesh textMesh = child.GetComponent<TextMesh>();
        if (textMesh == null)
        {
            textMesh = child.gameObject.AddComponent<TextMesh>();
        }

        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 64;
        textMesh.characterSize = healthLabelCharacterSize;
        textMesh.color = color;

        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sortingOrder = sortingOrder;
        }

        return textMesh;
    }

    private void RefreshHealthLabel()
    {
        if (!showHealthLabel)
        {
            if (healthLabelRoot != null)
            {
                healthLabelRoot.gameObject.SetActive(false);
            }
            return;
        }

        if (healthLabelRoot == null || healthLabelText == null || healthLabelShadow == null)
        {
            EnsureHealthLabel();
        }

        if (healthLabelRoot == null || healthLabelText == null || healthLabelShadow == null)
        {
            return;
        }

        string text = Mathf.Clamp(displayedHp, 0, maxHp).ToString();
        if (text != lastHealthLabelText)
        {
            healthLabelText.text = text;
            healthLabelShadow.text = text;
            lastHealthLabelText = text;
        }

        Color labelColor = ResolveHealthLabelColor();
        healthLabelText.color = labelColor;
        healthLabelShadow.color = healthLabelShadowColor;
        healthLabelText.characterSize = healthLabelCharacterSize;
        healthLabelShadow.characterSize = healthLabelCharacterSize;
        healthLabelRoot.gameObject.SetActive(true);
    }

    private void AnimateHealthLabelTo(int targetHp)
    {
        if (!showHealthLabel)
        {
            displayedHp = Mathf.Clamp(targetHp, 0, maxHp);
            return;
        }

        if (healthLabelRoot == null || healthLabelText == null || healthLabelShadow == null)
        {
            EnsureHealthLabel();
        }

        StopHealthLabelAnimation();
        healthLabelAnimationRoutine = StartCoroutine(AnimateHealthLabelRoutine(Mathf.Clamp(targetHp, 0, maxHp)));
    }

    private IEnumerator AnimateHealthLabelRoutine(int targetHp)
    {
        int startHp = Mathf.Clamp(displayedHp, 0, maxHp);
        float duration = Mathf.Max(0f, healthLabelAnimationSeconds);
        float elapsed = 0f;

        if (duration <= 0f || startHp == targetHp)
        {
            displayedHp = targetHp;
            RefreshHealthLabel();
            healthLabelAnimationRoutine = null;
            yield break;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            displayedHp = Mathf.RoundToInt(Mathf.Lerp(startHp, targetHp, t));
            RefreshHealthLabel();
            yield return null;
        }

        displayedHp = targetHp;
        RefreshHealthLabel();
        healthLabelAnimationRoutine = null;
    }

    private void StopHealthLabelAnimation()
    {
        if (healthLabelAnimationRoutine != null)
        {
            StopCoroutine(healthLabelAnimationRoutine);
            healthLabelAnimationRoutine = null;
        }
    }

    private void ShowDamagePopup(int damage)
    {
        if (!showHealthLabel || damage <= 0)
        {
            return;
        }

        StopDamagePopup();

        damagePopupRoot = new GameObject("DamagePopup").transform;
        damagePopupRoot.SetParent(transform, false);
        damagePopupRoot.localRotation = Quaternion.identity;
        damagePopupRoot.localScale = Vector3.one;

        string damageText = damage.ToString();
        damagePopupShadow = CreateDamagePopupText(
            "DamagePopupShadow",
            damageText,
            damagePopupShadowColor,
            healthLabelSortingOrder + 2,
            new Vector3(0.03f, -0.03f, 0f));
        damagePopupText = CreateDamagePopupText(
            "DamagePopupText",
            damageText,
            damagePopupColor,
            healthLabelSortingOrder + 3,
            Vector3.zero);

        Vector3 startPosition = CalculateDamagePopupStartPosition();
        damagePopupRoutine = StartCoroutine(DamagePopupRoutine(startPosition));
    }

    private TextMesh CreateDamagePopupText(string objectName, string text, Color color, int sortingOrder, Vector3 localPosition)
    {
        Transform child = new GameObject(objectName).transform;
        child.SetParent(damagePopupRoot, false);
        child.localPosition = localPosition;

        TextMesh textMesh = child.gameObject.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 64;
        textMesh.characterSize = healthLabelCharacterSize * damagePopupCharacterSizeMultiplier;
        textMesh.color = color;
        textMesh.text = text;

        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sortingOrder = sortingOrder;
        }

        return textMesh;
    }

    private IEnumerator DamagePopupRoutine(Vector3 startPosition)
    {
        float duration = Mathf.Max(0.01f, damagePopupSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (damagePopupRoot != null)
            {
                damagePopupRoot.position = startPosition + Vector3.up * (damagePopupRiseDistance * t);
                damagePopupRoot.rotation = Quaternion.identity;
            }

            yield return null;
        }

        ClearDamagePopupObject();
        damagePopupRoutine = null;
    }

    private Vector3 CalculateDamagePopupStartPosition()
    {
        Bounds bounds = bodyCollider != null && bodyCollider.enabled
            ? bodyCollider.bounds
            : new Bounds(transform.position, Vector3.one);

        return new Vector3(
            bounds.center.x + healthLabelOffset.x + damagePopupOffset.x,
            bounds.max.y + damagePopupOffset.y,
            transform.position.z);
    }

    private void StopDamagePopup()
    {
        if (damagePopupRoutine != null)
        {
            StopCoroutine(damagePopupRoutine);
            damagePopupRoutine = null;
        }

        ClearDamagePopupObject();
    }

    private void ClearDamagePopupObject()
    {
        if (damagePopupRoot != null)
        {
            Destroy(damagePopupRoot.gameObject);
        }

        damagePopupRoot = null;
        damagePopupText = null;
        damagePopupShadow = null;
    }

    private Color ResolveHealthLabelColor()
    {
        if (isDead || currentHp <= 0)
        {
            return deadLabelColor;
        }

        float health01 = maxHp > 0 ? currentHp / (float)maxHp : 0f;
        if (health01 <= 0.3f)
        {
            return dangerLabelColor;
        }

        if (health01 <= 0.6f)
        {
            return warningLabelColor;
        }

        return healthyLabelColor;
    }

    private void UpdateHealthLabelPosition()
    {
        if (!showHealthLabel || healthLabelRoot == null)
        {
            return;
        }

        Bounds bounds = bodyCollider != null
            ? bodyCollider.bounds
            : new Bounds(transform.position, Vector3.one);
        healthLabelRoot.position = new Vector3(
            bounds.center.x + healthLabelOffset.x,
            bounds.max.y + healthLabelOffset.y,
            transform.position.z);
        healthLabelRoot.rotation = Quaternion.identity;
    }

    private static TurnManager FindTurnManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindAnyObjectByType<TurnManager>();
#else
        return UnityEngine.Object.FindObjectOfType<TurnManager>();
#endif
    }

    private void FlashHit()
    {
        if (characterVisual != null)
        {
            characterVisual.PlayHitFlash(hitFlashColor, hitFlashSeconds);
            return;
        }

        if (spriteRenderer == null)
        {
            return;
        }

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
        }

        hitFlashRoutine = StartCoroutine(FlashHitRoutine());
    }

    private IEnumerator FlashHitRoutine()
    {
        spriteRenderer.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashSeconds);

        if (!isDead && spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        hitFlashRoutine = null;
    }
}
