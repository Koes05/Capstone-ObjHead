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

    private Rigidbody2D body;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider;
    private TurnCharacterController turnController;
    private CharacterVisual characterVisual;
    private Color originalColor = Color.white;
    private Coroutine hitFlashRoutine;
    private int currentHp;
    private bool isDead;

    public event Action<CharacterCombat> Died;

    public int CurrentHp => currentHp;
    public int MaxHp => maxHp;
    public float ThrowPower => throwPower;
    public float KnockbackResistance => knockbackResistance;
    public bool IsDead => isDead;
    public Vector2 KnockbackCenter => bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        turnController = GetComponent<TurnCharacterController>();
        bodyCollider = GetComponent<Collider2D>();
        characterVisual = GetComponent<CharacterVisual>();
        currentHp = maxHp;

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    public void ConfigureStats(int hp, float throwPowerMultiplier, float knockbackResistanceValue)
    {
        maxHp = Mathf.Max(1, hp);
        throwPower = Mathf.Max(0.1f, throwPowerMultiplier);
        knockbackResistance = Mathf.Max(0.1f, knockbackResistanceValue);
        currentHp = maxHp;
        isDead = false;
    }

    public void TakeDamage(int damage)
    {
        if (isDead || damage <= 0)
        {
            return;
        }

        currentHp = Mathf.Max(0, currentHp - damage);
        Debug.Log($"{name} HP: {currentHp}/{maxHp}");

        if (currentHp <= 0)
        {
            Die();
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
        if (isDead)
        {
            return;
        }

        isDead = true;
        currentHp = 0;
        turnController?.SetControlEnabled(false);
        turnController?.StopHorizontalMovement();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
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
