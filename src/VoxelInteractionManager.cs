using UnityEngine;

public class VoxelInteractionManager : MonoBehaviour
{
    public float maxCheckDistance = 25f;

    [Header("Interaction Settings")]
    [Tooltip("Radius of the area to remove.")]
    public float destructionRadius = 5f;
    
    [Header("Spray Paint Settings")]
    [Tooltip("Radius of the area to paint.")]
    public float sprayRadius = 3f;

    [Range(0, 255)]
    public byte paintColorIndex = 1;

    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    VoxelChunk hitChunk = hit.collider.GetComponentInParent<VoxelChunk>();
                    if (hitChunk != null)
                    {
                        ProcessVoxelSpray(hit, hitChunk, ray);
                    }
                }
            }
            else
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    VoxelChunk hitChunk = hit.collider.GetComponentInParent<VoxelChunk>();
                    if (hitChunk != null)
                    {
                        ProcessVoxelDestruction(hit, hitChunk, ray);
                    }
                }
            }
        }
    }

    private void ProcessVoxelDestruction(RaycastHit hit, VoxelChunk chunk, Ray worldRay)
    {
        Vector3 hitLocal = chunk.transform.InverseTransformPoint(hit.point);
        Vector3 dirLocal = chunk.transform.InverseTransformDirection(worldRay.direction).normalized;
        Vector3 startPosVoxel = hitLocal + dirLocal * 0.01f;

        // Perform robust voxel-by-voxel traversal (3D DDA)
        Vector3Int startVoxel = new Vector3Int(
            Mathf.FloorToInt(startPosVoxel.x),
            Mathf.FloorToInt(startPosVoxel.y),
            Mathf.FloorToInt(startPosVoxel.z)
        );

        Vector3Int? found = TraverseVoxelsDDA(chunk, startPosVoxel, dirLocal, maxCheckDistance);
        if (found.HasValue)
        {
            //Debug.Log($"Solid voxel hit at {found.Value} in {chunk.name}");

            //DestroyVoxelsInSphere(chunk, found.Value, destructionRadius);
            //chunk.SplitDisconnectedPieces();
            //chunk.RebuildMesh();

            chunk.DestroyVoxelsInSphere(found.Value, destructionRadius);
        }
        else
        {
            //Debug.Log($"No solid voxel found forward of hit in {chunk.name}");
        }
    }

    private void ProcessVoxelSpray(RaycastHit hit, VoxelChunk chunk, Ray worldRay)
    {
        Vector3 hitLocal = chunk.transform.InverseTransformPoint(hit.point);
        Vector3 dirLocal = chunk.transform.InverseTransformDirection(worldRay.direction).normalized;
        Vector3 startPosVoxel = hitLocal + dirLocal * 0.01f;

        Vector3Int? found = TraverseVoxelsDDA(chunk, startPosVoxel, dirLocal, maxCheckDistance);
        if (found.HasValue)
        {
            PaintExistingVoxelsInSphere(chunk, found.Value, sprayRadius, paintColorIndex);
        }
    }

    // 3D DDA voxel traversal (Amanatides & Woo) in voxel-space.
    private Vector3Int? TraverseVoxelsDDA(VoxelChunk chunk, Vector3 startPosVoxel, Vector3 dir, float maxDistance)
    {
        // Initial voxel coordinates
        int x = Mathf.FloorToInt(startPosVoxel.x);
        int y = Mathf.FloorToInt(startPosVoxel.y);
        int z = Mathf.FloorToInt(startPosVoxel.z);

        if (IsInsideChunk(chunk, x, y, z))
        {
            if (chunk.Blocks[x, y, z] != 0)
                return new Vector3Int(x, y, z);
        }

        int stepX = dir.x > 0 ? 1 : (dir.x < 0 ? -1 : 0);
        int stepY = dir.y > 0 ? 1 : (dir.y < 0 ? -1 : 0);
        int stepZ = dir.z > 0 ? 1 : (dir.z < 0 ? -1 : 0);

        float tDeltaX = (dir.x != 0f) ? Mathf.Abs(1f / dir.x) : float.PositiveInfinity;
        float tDeltaY = (dir.y != 0f) ? Mathf.Abs(1f / dir.y) : float.PositiveInfinity;
        float tDeltaZ = (dir.z != 0f) ? Mathf.Abs(1f / dir.z) : float.PositiveInfinity;

        float voxelBoundX = (stepX > 0) ? (x + 1f) : x;
        float voxelBoundY = (stepY > 0) ? (y + 1f) : y;
        float voxelBoundZ = (stepZ > 0) ? (z + 1f) : z;

        float tMaxX = (dir.x != 0f) ? (voxelBoundX - startPosVoxel.x) / dir.x : float.PositiveInfinity;
        float tMaxY = (dir.y != 0f) ? (voxelBoundY - startPosVoxel.y) / dir.y : float.PositiveInfinity;
        float tMaxZ = (dir.z != 0f) ? (voxelBoundZ - startPosVoxel.z) / dir.z : float.PositiveInfinity;

        if (tMaxX < 0f) tMaxX = 0f;
        if (tMaxY < 0f) tMaxY = 0f;
        if (tMaxZ < 0f) tMaxZ = 0f;

        float traveled = 0f;

        if (dir.sqrMagnitude < 1e-9f) return null;

        // Traverse
        while (traveled <= maxDistance)
        {
            // Advance along the smallest tMax
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                x += stepX;
                traveled = tMaxX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxX && tMaxY <= tMaxZ)
            {
                y += stepY;
                traveled = tMaxY;
                tMaxY += tDeltaY;
            }
            else
            {
                z += stepZ;
                traveled = tMaxZ;
                tMaxZ += tDeltaZ;
            }

            // Check bounds
            if (!IsInsideChunk(chunk, x, y, z))
            {
                break;
            }

            // Check the voxel value
            if (chunk.Blocks[x, y, z] != 0)
            {
                return new Vector3Int(x, y, z);
            }
        }

        return null;
    }

    private bool IsInsideChunk(VoxelChunk chunk, int x, int y, int z)
    {
        return x >= 0 && x < chunk.ChunkWidth &&
               y >= 0 && y < chunk.ChunkHeight &&
               z >= 0 && z < chunk.ChunkDepth;
    }

    private void DestroyVoxelsInSphere(VoxelChunk chunk, Vector3Int center, float radius)
    {
        int intRadius = Mathf.CeilToInt(radius);

        for (int x = -intRadius; x <= intRadius; x++)
        {
            for (int y = -intRadius; y <= intRadius; y++)
            {
                for (int z = -intRadius; z <= intRadius; z++)
                {
                    Vector3Int offset = new Vector3Int(x, y, z);

                    if (offset.magnitude <= radius)
                    {
                        Vector3Int pos = center + offset;

                        if (pos.x >= 0 && pos.x < chunk.ChunkWidth &&
                            pos.y >= 0 && pos.y < chunk.ChunkHeight &&
                            pos.z >= 0 && pos.z < chunk.ChunkDepth)
                        {
                            chunk.Blocks[pos.x, pos.y, pos.z] = 0;
                        }
                    }
                }
            }
        }
    }

    private void PaintExistingVoxelsInSphere(VoxelChunk chunk, Vector3Int center, float radius, byte colorIndex)
    {
        int intRadius = Mathf.CeilToInt(radius);

        for (int x = -intRadius; x <= intRadius; x++)
        {
            for (int y = -intRadius; y <= intRadius; y++)
            {
                for (int z = -intRadius; z <= intRadius; z++)
                {
                    Vector3Int offset = new Vector3Int(x, y, z);

                    if (offset.magnitude <= radius)
                    {
                        Vector3Int pos = center + offset;

                        if (pos.x >= 0 && pos.x < chunk.ChunkWidth &&
                            pos.y >= 0 && pos.y < chunk.ChunkHeight &&
                            pos.z >= 0 && pos.z < chunk.ChunkDepth)
                        {
                            if (chunk.Blocks[pos.x, pos.y, pos.z] != 0)
                            {
                                chunk.Blocks[pos.x, pos.y, pos.z] = colorIndex;
                                chunk.SetVoxelColor(pos.x, pos.y, pos.z, colorIndex);
                            }
                        }
                    }
                }
            }
        }

        chunk.Update3DTexture();
    }
}
