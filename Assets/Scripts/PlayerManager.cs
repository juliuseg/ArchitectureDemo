using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : ServiceUser<PlayerManager>
{
    [SerializeField] private PlayerStats stats;

    private PlayerMovement movement;
    private PlayerAttack attack;

    void Awake()
    {
        movement = new PlayerMovement(transform, stats.moveSpeed);
        attack = new PlayerAttack(stats.attackDamage);
        ServiceLocator.Register<PlayerManager>(this);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed) input.y += 1;
        if (keyboard.sKey.isPressed) input.y -= 1;
        if (keyboard.aKey.isPressed) input.x -= 1;
        if (keyboard.dKey.isPressed) input.x += 1;

        movement.Move(input);

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            attack.PerformAttack();
        }
    }
}

public class PlayerMovement
{
    readonly Transform transform;
    readonly float speed;

    public PlayerMovement(Transform transform, float speed)
    {
        this.transform = transform;
        this.speed = speed;
    }

    public void Move(Vector2 input)
    {
        if (input == Vector2.zero) return;

        Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
        transform.position += direction * speed * Time.deltaTime;
    }
}

public class PlayerAttack
{
    readonly int damage;

    public PlayerAttack(int damage)
    {
        this.damage = damage;
    }

    public void PerformAttack()
    {
        Debug.Log($"Player attacks for {damage} damage");
    }
}