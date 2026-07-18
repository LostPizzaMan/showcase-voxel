using System.Collections.Generic;
using UnityEngine;

public static class ColliderPool
{
    private static readonly Queue<BoxCollider> pool = new Queue<BoxCollider>();
    private static Transform poolParent;

    private static Transform PoolParent
    {
        get
        {
            if (poolParent == null)
            {
                GameObject existingPool = GameObject.Find("GlobalColliderPool");
                if (existingPool != null)
                {
                    poolParent = existingPool.transform;
                }
                else
                {
                    GameObject obj = new GameObject("GlobalColliderPool");
                    poolParent = obj.transform;
                }
            }
            return poolParent;
        }
    }

    public static BoxCollider GetCollider()
    {
        BoxCollider col;
        if (pool.Count > 0)
        {
            col = pool.Dequeue();
            col.gameObject.SetActive(true);
            return col;
        }

        // Create new collider if pool is empty
        GameObject obj = new GameObject("VoxelCollider");
        obj.transform.parent = PoolParent;

        // Reset local transforms relative to the pool parent
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;

        col = obj.AddComponent<BoxCollider>();
        return col;
    }

    public static void ReleaseCollider(BoxCollider col)
    {
        if (col == null) return;

        // Cleanup and reset state before pooling
        col.size = Vector3.one;
        col.center = Vector3.zero;

        // Re-parent to the pool manager for organization
        col.transform.parent = PoolParent;
        col.gameObject.SetActive(false);
        pool.Enqueue(col);
    }
}