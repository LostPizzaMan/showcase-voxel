using UnityEngine;

public class Spawner : MonoBehaviour
{
    [Header("Grid Settings")]
    public int rows = 5;
    public int columns = 5;
    public float spacingX = 2f;
    public float spacingY = 2f;

    [Header("Prefab")]
    public GameObject prefabToSpawn;

    [Header("Offset Grid to Center?")]
    public bool centerGrid = true;

    [Header("Prefab Rotation")]
    public Vector3 prefabRotation = new Vector3(0f, 0f, 0f);

    void Start()
    {
        if (prefabToSpawn == null)
        {
            Debug.LogError("No prefab assigned to GridSpawner.");
            return;
        }

        SpawnGrid();
    }

    void SpawnGrid()
    {
        Vector3 origin = transform.position;
        Vector3 offset = Vector3.zero;

        if (centerGrid)
        {
            float totalWidth = (columns - 1) * spacingX;
            float totalHeight = (rows - 1) * spacingY;
            offset = new Vector3(-totalWidth / 2f, 0, -totalHeight / 2f);
        }

        Quaternion rotation = Quaternion.Euler(prefabRotation);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Vector3 spawnPos = origin + offset + new Vector3(col * spacingX, 0, row * spacingY);
                Instantiate(prefabToSpawn, spawnPos, rotation, transform);
            }
        }
    }
}
