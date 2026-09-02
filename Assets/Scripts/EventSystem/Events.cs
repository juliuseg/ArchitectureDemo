public interface IEvent { }

public struct PlayerHealthChanged : IEvent
{
    public int CurrentHealth;
    public int MaxHealth;
}

public struct PlayerDied : IEvent { }

public struct PointsChanged : IEvent
{
    public int NewTotal;
}