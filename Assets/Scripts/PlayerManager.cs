using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : ServiceUser<PlayerManager>
{
    [SerializeField] private PlayerStats stats;

    private PlayerMovement movement;
    private PlayerAttack attack;
    private Vector2 moveInput;

    void Awake()
    {
        movement = new PlayerMovement(transform, stats.moveSpeed);
        attack = new PlayerAttack(stats.attackDamage);
        ServiceLocator.Register<PlayerManager>(this);
    }

    void Update()
    {
        movement.Move(moveInput);
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnAttack(InputValue value)
    {
        attack.PerformAttack();
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