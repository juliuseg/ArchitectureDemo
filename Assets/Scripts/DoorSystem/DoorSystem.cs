using UnityEngine;

public class DoorSystem : MonoBehaviour
{
    [SerializeField] private Transform door;
    [SerializeField] private float speed = 1f;
    [SerializeField] private float distance = 2f;

    private Vector3 closedPosition;
    private Vector3 openPosition;
    private Vector3 targetPosition;

    private void Start()
    {
        closedPosition = door.position;
        openPosition = closedPosition + Vector3.down * distance;
        targetPosition = closedPosition;
    }

    private void Update()
    {
        door.position = Vector3.MoveTowards(door.position, targetPosition, speed * Time.deltaTime);
    }

    public void Open()
    {
        Debug.Log("Open");
        targetPosition = openPosition;
    }

    public void Close()
    {
        targetPosition = closedPosition;
    }
}