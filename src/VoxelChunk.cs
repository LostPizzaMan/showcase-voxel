using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VoxReader;
using VoxReader.Interfaces;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

public class VoxelChunk : MonoBehaviour
{
    public int ChunkWidth;
    public int ChunkHeight;
    public int ChunkDepth;

    public byte[,,] Blocks;

    [Header("Debris")]
    public float debrisForce = 5f;
    public float debrisTorque = 3f;

    [Header("Texture")]
    public Texture3D VoxelTexture3D;
    public Texture2D PaletteTexture;

    [Header("Voxel")]
    public MeshRenderer VoxelRenderer;
    public MeshFilter VoxelFilter;
    public GameObject VoxelRendererGO;

    [Header("Mesh")]
    public MeshFilter MeshFilter = null;

    [Range(1, 16)]
    public int colliderResolution = 3;

    [Header("Voxel")]
    public string VoxFileName = "";

    private Mesh mesh;
    private Rigidbody rb;

    private List<Vector3> verts = new List<Vector3>();
    private List<int> tris = new List<int>();
    private List<Color32> colors = new List<Color32>();

    private Color32[] palette;

    public bool SavePaletteDebugPng = false;
    public bool IsStatic = false;
    public bool BuildColliders = true;

    void Start()
    {
        if (!string.IsNullOrEmpty(VoxFileName))
        {
            LoadVoxFile();
            RebuildMesh();
        }
    }

    private void LoadVoxFile()
    {
        IVoxFile voxFile = VoxReader.VoxReader.Read($"./Assets/Resources/Vox/{VoxFileName}.vox");
        IModel[] models = voxFile.Models;

        if (models == null || models.Length == 0)
        {
            Debug.LogError($"No models found in {VoxFileName}.vox.");
            return;
        }

        IModel model = models[0];
        Voxel[] voxels = model.Voxels;

        if (voxels == null || voxels.Length == 0)
        {
            Debug.LogError("No voxels in model.");
            return;
        }

        // Calculate bounds and offset the voxels to a 0-based coordinate system
        int minX = voxels.Min(v => v.GlobalPosition.X);
        int minY = voxels.Min(v => v.GlobalPosition.Y);
        int minZ = voxels.Min(v => v.GlobalPosition.Z);

        int maxX = voxels.Max(v => v.GlobalPosition.X);
        int maxY = voxels.Max(v => v.GlobalPosition.Y);
        int maxZ = voxels.Max(v => v.GlobalPosition.Z);

        ChunkWidth = maxX - minX + 1;
        ChunkHeight = maxY - minY + 1;
        ChunkDepth = maxZ - minZ + 1;

        //Debug.Log($"Voxel bounds - width: {ChunkWidth}, height: {ChunkHeight}, depth: {ChunkDepth}");

        Blocks = new byte[ChunkWidth, ChunkHeight, ChunkDepth];
        palette = voxFile.Palette.Colors.Select(c => new Color32(c.R, c.G, c.B, c.A)).ToArray();

        foreach (Voxel voxel in voxels)
        {
            var pos = voxel.GlobalPosition;

            int x = pos.X - minX;
            int y = pos.Y - minY;
            int z = pos.Z - minZ;

            Blocks[x, y, z] = (byte)voxel.ColorIndex;
        }

        InitializeSections();
    }

    public void Create3DTexture()
    {
        if (Blocks == null || palette == null)
        {
            Debug.LogError("Voxel Blocks or Palette is not initialized. Cannot create Texture3D.");
            return;
        }

        // Create the voxel texture.
        VoxelTexture3D = new Texture3D(ChunkWidth, ChunkHeight, ChunkDepth, TextureFormat.RGBA32, false);
        VoxelTexture3D.wrapMode = TextureWrapMode.Clamp;

        // Create a 1D color array to populate the Texture3D
        Color[] colorArray = new Color[ChunkWidth * ChunkHeight * ChunkDepth];
        int index = 0;

        // The Texture3D requires colors to be loaded in an X, Y, Z loop (depth-first for the 1D array)
        for (int z = 0; z < ChunkDepth; z++)
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                for (int x = 0; x < ChunkWidth; x++)
                {
                    // Get the color index from the Blocks array
                    byte colorIndex = Blocks[x, y, z];

                    // MagicaVoxel palette indices start at 1 for the first color, 0 is typically empty/transparent
                    if (colorIndex > 0 && colorIndex < palette.Length)
                    {
                        colorArray[index] = palette[colorIndex];
                    }
                    else
                    {
                        // If index is 0 or out of bounds, set to transparent (empty voxel)
                        colorArray[index] = new Color(0, 0, 0, 0);
                    }

                    index++;
                }
            }
        }

        // Apply the 1D color array to the Texture3D
        VoxelTexture3D.SetPixels(colorArray);
        VoxelTexture3D.Apply();
        ApplyTextureToShader();
        Texture3DAtlas.SaveTexture3D(VoxelTexture3D, "VoxelTexture3D");

        Debug.Log($"Successfully created Texture3D with dimensions: {ChunkWidth}x{ChunkHeight}x{ChunkDepth}");
    }

    public void Create3DTextureWithPalette()
    {
        if (Blocks == null || palette == null)
        {
            Debug.LogError("Voxel Blocks or Palette is not initialized. Cannot create Texture3D.");
            return;
        }

        // --- 1. Create the 3D voxel texture storing only palette indices ---
        VoxelTexture3D = new Texture3D(ChunkWidth, ChunkHeight, ChunkDepth, TextureFormat.R8, false);
        VoxelTexture3D.wrapMode = TextureWrapMode.Clamp;

        // R8 stores the raw palette index; upload bytes directly instead of Color structs
        byte[] voxelData = new byte[ChunkWidth * ChunkHeight * ChunkDepth];
        int index = 0;

        for (int z = 0; z < ChunkDepth; z++)
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                for (int x = 0; x < ChunkWidth; x++)
                {
                    voxelData[index++] = Blocks[x, y, z]; // 0 = empty
                }
            }
        }

        VoxelTexture3D.SetPixelData(voxelData, 0);
        VoxelTexture3D.Apply(false);

        // --- 2. Create the 1x256 palette texture ---
        PaletteTexture = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        PaletteTexture.wrapMode = TextureWrapMode.Clamp;

        Color32[] paletteColors = new Color32[256];
        for (int i = 1; i < 256; i++)
        {
            // Index 0 is air/empty, everything else is a color (if it exists in the palette)
            paletteColors[i] = i < palette.Length ? palette[i] : (Color32)Color.clear;
        }
        PaletteTexture.SetPixels32(paletteColors);

        PaletteTexture.filterMode = FilterMode.Point;
        PaletteTexture.Apply();

        if (SavePaletteDebugPng)
        {
            byte[] bytes = PaletteTexture.EncodeToPNG();
            string filePath = Path.Combine(Application.dataPath, $"PaletteTexture.png");
            File.WriteAllBytes(filePath, bytes);
        }

        // --- 3. Apply textures to material/shader ---
        ApplyTextureToShader();

        Debug.Log($"3D Texture ({ChunkWidth}x{ChunkHeight}x{ChunkDepth}) and 1x255 Palette created!");
    }

    public void SetupVoxelRenderer()
    {
        VoxelRendererGO = new GameObject("VoxelRenderer");
        VoxelRendererGO.transform.parent = transform;

        // Set local position to zero, so the BoxCollider's center and size define the local space box
        VoxelRendererGO.transform.localPosition = new Vector3(ChunkWidth / 2f, ChunkHeight / 2f, ChunkDepth / 2f);
        VoxelRendererGO.transform.localRotation = Quaternion.identity;
        VoxelRendererGO.transform.localScale *= 10f;

        // Setup Mesh Filter and Renderer
        VoxelFilter = VoxelRendererGO.AddComponent<MeshFilter>();

        // Create a temporary cube just to get its mesh
        var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
        GameObject.Destroy(tempCube);

        // Assign that mesh to your object
        VoxelFilter.sharedMesh = cubeMesh;

        VoxelRenderer = VoxelRendererGO.AddComponent<MeshRenderer>();
        VoxelRenderer.material = gameObject.GetComponent<MeshRenderer>().material;

        SetExactScale();
    }

    public void SetExactScale()
    {
        VoxelRenderer.transform.localScale = new Vector3(ChunkWidth, ChunkHeight, ChunkDepth);
    }

    public void ApplyTextureToShader()
    {
        var rend = VoxelRenderer;

        if (rend == null)
        {
            Debug.LogError("MeshRenderer component not found or initialized on VoxelChunk object.");
            return;
        }

        if (VoxelTexture3D == null)
        {
            Debug.LogError("VoxelTexture3D is null. Cannot apply to shader.");
            return;
        }

        Material material = rend.material;

        material.SetTexture("_Voxels", VoxelTexture3D);
        material.SetTexture("_PaletteTex", PaletteTexture);
        material.SetVector("_VoxelDimensions", new Vector4(ChunkWidth, ChunkHeight, ChunkDepth, 0));

        Debug.Log("VoxelTexture3D and VoxelDimensions assigned to shader material.");
    }

    public void SetVoxelColor(int x, int y, int z, byte newColorIndex)
    {
        if (Blocks == null || VoxelTexture3D == null || palette == null)
        {
            Debug.LogError("Voxel data or Texture3D is not initialized.");
            return;
        }

        // Check bounds
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkDepth)
        {
            Debug.LogWarning($"Voxel coordinates ({x}, {y}, {z}) are out of bounds.");
            return;
        }

        float normalizedIndex = 0;

        if (newColorIndex > 0)
        {
            normalizedIndex = newColorIndex / 255f;
        }

        VoxelTexture3D.SetPixel(x, y, z, new Color(normalizedIndex, 0, 0));
    }

    public void Update3DTexture()
    {
        VoxelTexture3D.Apply();
    }

    public void RebuildMesh()
    {
        GenerateColliders(colliderResolution);
    }

    byte GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkDepth)
            return 0;
        return Blocks[x, y, z];
    }

    // ------------------------------------------------------------
    // COLLIDER GREEDY MESH (3D Box merge)
    // ------------------------------------------------------------

    // Stores currently active colliders and their bounds/hash for THIS chunk
    private List<ColliderInfo> colliders = new List<ColliderInfo>();

    class ColliderInfo
    {
        public BoxCollider Collider;
        public Bounds Bounds;
        public int Hash;
        public ColliderInfo(BoxCollider collider, Bounds bounds, int hash)
        {
            Collider = collider;
            Bounds = bounds;
            Hash = hash;
        }
    }

    int HashBounds(Bounds b)
    {
        Vector3 c = b.center * 10000f;
        Vector3 s = b.size * 10000f;
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + c.GetHashCode();
            hash = hash * 31 + s.GetHashCode();
            return hash;
        }
    }

    public void GenerateColliders(int scaleFactor)
    {
        if (!BuildColliders || Blocks == null) return;
        if (scaleFactor <= 0) scaleFactor = 1;

        // Static chunks have no Rigidbody; their colliders are static colliders
        bool wasKinematic = false;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
        }

        int cw = ChunkWidth;
        int ch = ChunkHeight;
        int cd = ChunkDepth;

        int lowResWidth = Mathf.CeilToInt((float)cw / scaleFactor);
        int lowResHeight = Mathf.CeilToInt((float)ch / scaleFactor);
        int lowResDepth = Mathf.CeilToInt((float)cd / scaleFactor);

        bool[,,] lowResBlocks = new bool[lowResWidth, lowResHeight, lowResDepth];

        // === Step 1: Downsample ===
        for (int lx = 0; lx < lowResWidth; lx++)
            for (int ly = 0; ly < lowResHeight; ly++)
                for (int lz = 0; lz < lowResDepth; lz++)
                {
                    bool isGroupSolid = false;
                    int sx = lx * scaleFactor;
                    int sy = ly * scaleFactor;
                    int sz = lz * scaleFactor;

                    for (int ix = 0; ix < scaleFactor && !isGroupSolid; ix++)
                        for (int iy = 0; iy < scaleFactor && !isGroupSolid; iy++)
                            for (int iz = 0; iz < scaleFactor && !isGroupSolid; iz++)
                            {
                                int hx = sx + ix;
                                int hy = sy + iy;
                                int hz = sz + iz;
                                if (hx < cw && hy < ch && hz < cd && Blocks[hx, hy, hz] != 0)
                                    isGroupSolid = true;
                            }

                    lowResBlocks[lx, ly, lz] = isGroupSolid;
                }

        // === Step 2: Greedy Merge + Tight Bounds ===
        bool[,,] visited = new bool[lowResWidth, lowResHeight, lowResDepth];
        List<Bounds> newBounds = new List<Bounds>();

        for (int x = 0; x < lowResWidth; x++)
            for (int y = 0; y < lowResHeight; y++)
                for (int z = 0; z < lowResDepth; z++)
                {
                    if (visited[x, y, z] || !lowResBlocks[x, y, z]) continue;

                    int sizeX = 1, sizeY = 1, sizeZ = 1;

                    // Expand X
                    while (x + sizeX < lowResWidth)
                    {
                        bool canExpand = true;
                        for (int yy = 0; yy < sizeY && canExpand; yy++)
                            for (int zz = 0; zz < sizeZ && canExpand; zz++)
                                if (!lowResBlocks[x + sizeX, y + yy, z + zz] || visited[x + sizeX, y + yy, z + zz])
                                    canExpand = false;
                        if (!canExpand) break;
                        sizeX++;
                    }

                    // Expand Z
                    while (z + sizeZ < lowResDepth)
                    {
                        bool canExpand = true;
                        for (int ix = 0; ix < sizeX && canExpand; ix++)
                            for (int iy = 0; iy < sizeY && canExpand; iy++)
                                if (!lowResBlocks[x + ix, y + iy, z + sizeZ] || visited[x + ix, y + iy, z + sizeZ])
                                    canExpand = false;
                        if (!canExpand) break;
                        sizeZ++;
                    }

                    // Expand Y
                    while (y + sizeY < lowResHeight)
                    {
                        bool canExpand = true;
                        for (int ix = 0; ix < sizeX && canExpand; ix++)
                            for (int iz = 0; iz < sizeZ && canExpand; iz++)
                                if (!lowResBlocks[x + ix, y + sizeY, z + iz] || visited[x + ix, y + sizeY, z + iz])
                                    canExpand = false;
                        if (!canExpand) break;
                        sizeY++;
                    }

                    // Mark as visited
                    for (int ix = 0; ix < sizeX; ix++)
                        for (int iy = 0; iy < sizeY; iy++)
                            for (int iz = 0; iz < sizeZ; iz++)
                                visited[x + ix, y + iy, z + iz] = true;

                    // Compute high-res min/max (Tight Bounds)
                    int hr_xMin = x * scaleFactor;
                    int hr_yMin = y * scaleFactor;
                    int hr_zMin = z * scaleFactor;

                    int hr_xMax = Mathf.Min((x + sizeX) * scaleFactor, cw);
                    int hr_yMax = Mathf.Min((y + sizeY) * scaleFactor, ch);
                    int hr_zMax = Mathf.Min((z + sizeZ) * scaleFactor, cd);

                    int hx_min = cw, hy_min = ch, hz_min = cd;
                    int hx_max = 0, hy_max = 0, hz_max = 0;
                    bool foundSolid = false;

                    for (int hx = hr_xMin; hx < hr_xMax; hx++)
                        for (int hy = hr_yMin; hy < hr_yMax; hy++)
                            for (int hz = hr_zMin; hz < hr_zMax; hz++)
                            {
                                if (Blocks[hx, hy, hz] != 0)
                                {
                                    foundSolid = true;
                                    if (hx < hx_min) hx_min = hx;
                                    if (hy < hy_min) hy_min = hy;
                                    if (hz < hz_min) hz_min = hz;
                                    if (hx + 1 > hx_max) hx_max = hx + 1;
                                    if (hy + 1 > hy_max) hy_max = hy + 1;
                                    if (hz + 1 > hz_max) hz_max = hz + 1;
                                }
                            }

                    if (!foundSolid) continue;

                    // Final Bounds computation
                    Vector3 start = new Vector3(hx_min, hy_min, hz_min);
                    Vector3 end = new Vector3(hx_max, hy_max, hz_max);
                    Bounds b = new Bounds();
                    b.center = start + (end - start) * 0.5f;
                    b.size = end - start;
                    newBounds.Add(b);
                }

        // === Step 3: Sync Colliders ===
        var newMap = new Dictionary<int, Bounds>(newBounds.Count);
        foreach (var b in newBounds)
        {
            int hash = HashBounds(b);
            if (!newMap.ContainsKey(hash))
                newMap.Add(hash, b);
        }

        // Remove obsolete colliders
        for (int i = colliders.Count - 1; i >= 0; i--)
        {
            if (!newMap.ContainsKey(colliders[i].Hash))
            {
                ColliderPool.ReleaseCollider(colliders[i].Collider);
                colliders.RemoveAt(i);
            }
            else
            {
                // Mark existing hash as processed
                newMap.Remove(colliders[i].Hash);
            }
        }

        // Add missing colliders
        foreach (var kvp in newMap)
        {
            int hash = kvp.Key;
            Bounds b = kvp.Value;

            BoxCollider col = ColliderPool.GetCollider();

            // Set local relative to the CHUNK (parent)
            col.transform.parent = transform;
            col.transform.localPosition = Vector3.zero;
            col.transform.localRotation = Quaternion.identity;
            col.transform.localScale = Vector3.one;

            col.center = b.center;
            col.size = b.size;
            colliders.Add(new ColliderInfo(col, b, hash));
        }

        if (rb != null)
            rb.isKinematic = wasKinematic;
    }

    public void SplitDisconnectedPieces()
    {
        if (Blocks == null) return;

        int[,,] labels = new int[ChunkWidth, ChunkHeight, ChunkDepth];
        int currentLabel = 1;
        Dictionary<int, List<Vector3Int>> components = new Dictionary<int, List<Vector3Int>>();

        int[] dx = { 1, -1, 0, 0, 0, 0 };
        int[] dy = { 0, 0, 1, -1, 0, 0 };
        int[] dz = { 0, 0, 0, 0, 1, -1 };

        // Label connected components
        for (int x = 0; x < ChunkWidth; x++)
            for (int y = 0; y < ChunkHeight; y++)
                for (int z = 0; z < ChunkDepth; z++)
                {
                    if (Blocks[x, y, z] == 0 || labels[x, y, z] != 0) continue;

                    Queue<Vector3Int> queue = new Queue<Vector3Int>();
                    queue.Enqueue(new Vector3Int(x, y, z));
                    labels[x, y, z] = currentLabel;

                    List<Vector3Int> componentVoxels = new List<Vector3Int> { new Vector3Int(x, y, z) };

                    while (queue.Count > 0)
                    {
                        Vector3Int v = queue.Dequeue();
                        for (int i = 0; i < 6; i++)
                        {
                            int nx = v.x + dx[i];
                            int ny = v.y + dy[i];
                            int nz = v.z + dz[i];
                            if (nx < 0 || nx >= ChunkWidth || ny < 0 || ny >= ChunkHeight || nz < 0 || nz >= ChunkDepth) continue;
                            if (Blocks[nx, ny, nz] == 0 || labels[nx, ny, nz] != 0) continue;

                            labels[nx, ny, nz] = currentLabel;
                            queue.Enqueue(new Vector3Int(nx, ny, nz));
                            componentVoxels.Add(new Vector3Int(nx, ny, nz));
                        }
                    }

                    components[currentLabel] = componentVoxels;
                    currentLabel++;
                }

        if (components.Count <= 1) return;

        // Identify largest component
        int largestLabel = components.Aggregate((l, r) => l.Value.Count > r.Value.Count ? l : r).Key;

        // Prepare new chunks from smaller components
        foreach (var kvp in components)
        {
            if (kvp.Key == largestLabel) continue;

            List<Vector3Int> voxels = kvp.Value;

            // Determine local bounds
            int minX = voxels.Min(v => v.x);
            int minY = voxels.Min(v => v.y);
            int minZ = voxels.Min(v => v.z);
            int maxX = voxels.Max(v => v.x);
            int maxY = voxels.Max(v => v.y);
            int maxZ = voxels.Max(v => v.z);

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int depth = maxZ - minZ + 1;

            // Create new Blocks array
            byte[,,] newBlocks = new byte[width, height, depth];
            foreach (var v in voxels)
            {
                int lx = v.x - minX;
                int ly = v.y - minY;
                int lz = v.z - minZ;
                newBlocks[lx, ly, lz] = Blocks[v.x, v.y, v.z];

                // Remove from original chunk
                Blocks[v.x, v.y, v.z] = 0;
                SetVoxelColor(v.x, v.y, v.z, 0);
            }

            if (newBlocks.Length <= 4)
            {
                continue;
            }

            // Convert local voxel offset to world position using original chunk transform
            Vector3 localOffset = new Vector3(minX, minY, minZ);
            Vector3 worldPos = transform.TransformPoint(localOffset);

            // Create new chunk
            GameObject newGO = new GameObject($"{name}_Piece_{kvp.Key}");
            newGO.transform.position = worldPos;
            newGO.transform.rotation = transform.rotation;
            newGO.transform.localScale = transform.localScale;

            VoxelChunk newChunk = newGO.AddComponent<VoxelChunk>();
            newChunk.ChunkWidth = width;
            newChunk.ChunkHeight = height;
            newChunk.ChunkDepth = depth;
            newChunk.Blocks = newBlocks;
            newChunk.palette = palette;
            newChunk.MeshFilter = newGO.AddComponent<MeshFilter>();

            var renderer = newGO.AddComponent<MeshRenderer>();
            renderer.material = GetComponent<MeshRenderer>().material;

            newChunk.InitializeSections();
            newChunk.RebuildMesh();
        }

        // Apply the changes to the GPU
        VoxelTexture3D.Apply();
    }

    public void InitializeFromData(byte[,,] blocks, Color32[] paletteColors, bool isStatic = false, bool buildColliders = true)
    {
        IsStatic = isStatic;
        BuildColliders = buildColliders;
        ChunkWidth = blocks.GetLength(0);
        ChunkHeight = blocks.GetLength(1);
        ChunkDepth = blocks.GetLength(2);
        Blocks = blocks;
        palette = paletteColors;

        if (MeshFilter == null)
        {
            MeshFilter = gameObject.GetComponent<MeshFilter>();
            if (MeshFilter == null)
                MeshFilter = gameObject.AddComponent<MeshFilter>();
        }

        InitializeSections();
        RebuildMesh();
    }

    private const int SectionSize = 4;
    private const byte MultiComponentBit = 1 << 6;

    private static readonly int[] neighborOffsetX = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] neighborOffsetY = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] neighborOffsetZ = { 0, 0, 0, 0, 1, -1 };

    private static readonly Vector3Int[] dirs = {
        new Vector3Int(1,0,0), new Vector3Int(-1,0,0), new Vector3Int(0,1,0),
        new Vector3Int(0,-1,0), new Vector3Int(0,0,1), new Vector3Int(0,0,-1)
    };

    private int secW, secH, secD;

    /// Bits 0-5: Neighbor connection flags (+x, -x, +y, -y, +z, -z)
    /// Bit 6: Section contains multiple disconnected voxel components
    private byte[,,] sectionBytes;
    private bool[,,] tempSectionVisited = new bool[SectionSize, SectionSize, SectionSize];

    // ------------------------------------------------------------------
    // I. INITIALIZATION AND SECTION HELPERS
    // ------------------------------------------------------------------

    private void InitializeSections()
    {
        if (!IsStatic)
            rb = gameObject.AddComponent<Rigidbody>();

        secW = Mathf.CeilToInt((float)ChunkWidth / SectionSize);
        secH = Mathf.CeilToInt((float)ChunkHeight / SectionSize);
        secD = Mathf.CeilToInt((float)ChunkDepth / SectionSize);

        sectionBytes = new byte[secW, secH, secD];

        UpdateAllSectionNeighbors();
        SetupVoxelRenderer();
        //Create3DTexture();
        Create3DTextureWithPalette();
    }

    private Vector3Int GetSectionIndex(int x, int y, int z)
    {
        return new Vector3Int(x / SectionSize, y / SectionSize, z / SectionSize);
    }

    private bool SectionHasVoxels(Vector3Int sec)
    {
        if (sec.x < 0 || sec.y < 0 || sec.z < 0 || sec.x >= secW || sec.y >= secH || sec.z >= secD)
            return false;

        int startX = sec.x * SectionSize;
        int startY = sec.y * SectionSize;
        int startZ = sec.z * SectionSize;
        int endX = Mathf.Min(startX + SectionSize, ChunkWidth);
        int endY = Mathf.Min(startY + SectionSize, ChunkHeight);
        int endZ = Mathf.Min(startZ + SectionSize, ChunkDepth);

        for (int x = startX; x < endX; x++)
            for (int y = startY; y < endY; y++)
                for (int z = startZ; z < endZ; z++)
                    if (Blocks[x, y, z] != 0) return true;

        return false;
    }

    private int VoxelFloodFillConstrained(Vector3Int startVoxel, int startX, int startY, int startZ, int endX, int endY, int endZ)
    {
        if (Blocks[startVoxel.x, startVoxel.y, startVoxel.z] == 0) return 0;

        bool[,,] visited = tempSectionVisited;

        Array.Clear(visited, 0, SectionSize * SectionSize * SectionSize);

        int count = 0;
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        queue.Enqueue(startVoxel);

        // Convert starting voxel to local coordinates and mark as visited
        int localStartX = startVoxel.x - startX;
        int localStartY = startVoxel.y - startY;
        int localStartZ = startVoxel.z - startZ;
        visited[localStartX, localStartY, localStartZ] = true;

        while (queue.Count > 0)
        {
            Vector3Int v = queue.Dequeue();
            count++;

            // Iterate using the global static offset arrays
            for (int i = 0; i < 6; i++)
            {
                int nx = v.x + neighborOffsetX[i];
                int ny = v.y + neighborOffsetY[i];
                int nz = v.z + neighborOffsetZ[i];

                // Check bounds: Must be within the SECTION
                if (nx < startX || ny < startY || nz < startZ || nx >= endX || ny >= endY || nz >= endZ) continue;

                // Convert neighbor to local coordinates
                int localNX = nx - startX;
                int localNY = ny - startY;
                int localNZ = nz - startZ;

                // Check the local visited array (fast and isolated)
                if (visited[localNX, localNY, localNZ]) continue;
                if (Blocks[nx, ny, nz] == 0) continue;

                // Mark as visited and enqueue
                visited[localNX, localNY, localNZ] = true;
                queue.Enqueue(new Vector3Int(nx, ny, nz));
            }
        }
        return count;
    }

    private bool SectionHasMultipleComponents(Vector3Int sec)
    {
        // If the section is out of bounds, treat as continuous (no blocks)
        if (sec.x < 0 || sec.y < 0 || sec.z < 0 || sec.x >= secW || sec.y >= secH || sec.z >= secD) return false;

        int startX = sec.x * SectionSize;
        int startY = sec.y * SectionSize;
        int startZ = sec.z * SectionSize;
        int endX = Mathf.Min(startX + SectionSize, ChunkWidth);
        int endY = Mathf.Min(startY + SectionSize, ChunkHeight);
        int endZ = Mathf.Min(startZ + SectionSize, ChunkDepth);

        Vector3Int? startVoxel = null;
        int totalVoxels = 0;

        // Find all non-zero voxels and the first voxel
        for (int x = startX; x < endX; x++)
            for (int y = startY; y < endY; y++)
                for (int z = startZ; z < endZ; z++)
                    if (Blocks[x, y, z] != 0)
                    {
                        totalVoxels++;
                        if (startVoxel == null)
                            startVoxel = new Vector3Int(x, y, z);
                    }

        if (totalVoxels <= 1) return false; // Empty or single-voxel section is continuous

        // Voxel BFS to find the size of the first component (using fixed helper)
        int firstComponentCount = VoxelFloodFillConstrained(startVoxel.Value, startX, startY, startZ, endX, endY, endZ);

        // If the first component doesn't account for all voxels, it's discontinuous
        return (firstComponentCount != totalVoxels);
    }

    // ------------------------------------------------------------------
    // II. NEIGHBOR UPDATES (Fast section-level checks)
    // ------------------------------------------------------------------

    private void CalculateSectionNeighbors(Vector3Int sec, bool multiComponent)
    {
        if (sec.x < 0 || sec.y < 0 || sec.z < 0 || sec.x >= secW || sec.y >= secH || sec.z >= secD)
            return;

        byte flags = 0;
        int startX = sec.x * SectionSize;
        int startY = sec.y * SectionSize;
        int startZ = sec.z * SectionSize;
        int endX = Mathf.Min(startX + SectionSize, ChunkWidth);
        int endY = Mathf.Min(startY + SectionSize, ChunkHeight);
        int endZ = Mathf.Min(startZ + SectionSize, ChunkDepth);

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3Int neighborSec = sec + dirs[i];
            if (neighborSec.x < 0 || neighborSec.y < 0 || neighborSec.z < 0 ||
                neighborSec.x >= secW || neighborSec.y >= secH || neighborSec.z >= secD)
                continue;

            bool connected = false;

            switch (i)
            {
                case 0: // +x
                    if (endX < ChunkWidth)
                        for (int y = startY; y < endY && !connected; y++)
                            for (int z = startZ; z < endZ && !connected; z++)
                                if (Blocks[endX - 1, y, z] != 0 && Blocks[endX, y, z] != 0) connected = true;
                    break;
                case 1: // -x
                    if (startX > 0)
                        for (int y = startY; y < endY && !connected; y++)
                            for (int z = startZ; z < endZ && !connected; z++)
                                if (Blocks[startX, y, z] != 0 && Blocks[startX - 1, y, z] != 0) connected = true;
                    break;
                case 2: // +y
                    if (endY < ChunkHeight)
                        for (int x = startX; x < endX && !connected; x++)
                            for (int z = startZ; z < endZ && !connected; z++)
                                if (Blocks[x, endY - 1, z] != 0 && Blocks[x, endY, z] != 0) connected = true;
                    break;
                case 3: // -y
                    if (startY > 0)
                        for (int x = startX; x < endX && !connected; x++)
                            for (int z = startZ; z < endZ && !connected; z++)
                                if (Blocks[x, startY, z] != 0 && Blocks[x, startY - 1, z] != 0) connected = true;
                    break;
                case 4: // +z
                    if (endZ < ChunkDepth)
                        for (int x = startX; x < endX && !connected; x++)
                            for (int y = startY; y < endY && !connected; y++)
                                if (Blocks[x, y, endZ - 1] != 0 && Blocks[x, y, endZ] != 0) connected = true;
                    break;
                case 5: // -z
                    if (startZ > 0)
                        for (int x = startX; x < endX && !connected; x++)
                            for (int y = startY; y < endY && !connected; y++)
                                if (Blocks[x, y, startZ] != 0 && Blocks[x, y, startZ - 1] != 0) connected = true;
                    break;
            }

            if (connected)
                flags |= (byte)(1 << i);
        }

        if (multiComponent)
        {
            // Calculate Multi-Component Flag (Bit 6)
            if (SectionHasMultipleComponents(sec))
            {
                flags |= MultiComponentBit;
            }
        }

        sectionBytes[sec.x, sec.y, sec.z] = flags;
    }

    private void UpdateAllSectionNeighbors()
    {
        for (int x = 0; x < secW; x++)
            for (int y = 0; y < secH; y++)
                for (int z = 0; z < secD; z++)
                    CalculateSectionNeighbors(new Vector3Int(x, y, z), false);
    }

    // ------------------------------------------------------------------
    // III. SPLITTING LOGIC (Optimized Section-level component finding)
    // ------------------------------------------------------------------
    
    private List<List<Vector3Int>> FindDisconnectedSectionGroups(HashSet<Vector3Int> startSections)
    {
        bool[,,] visited = new bool[secW, secH, secD];
        List<List<Vector3Int>> allGroups = new List<List<Vector3Int>>();

        // Use a list to ensure we iterate over the start sections reliably
        foreach (Vector3Int startSec in startSections.OrderBy(v => v.y).ThenBy(v => v.x).ThenBy(v => v.z))
        {
            // Check bounds (in case neighbors outside chunk size were added)
            if (startSec.x < 0 || startSec.y < 0 || startSec.z < 0 ||
                startSec.x >= secW || startSec.y >= secH || startSec.z >= secD)
                continue;

            // Skip if already visited or empty
            if (visited[startSec.x, startSec.y, startSec.z] || !SectionHasVoxels(startSec))
                continue;

            // Found a new group, start BFS
            List<Vector3Int> currentGroup = new List<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            queue.Enqueue(startSec);
            visited[startSec.x, startSec.y, startSec.z] = true;

            while (queue.Count > 0)
            {
                Vector3Int sec = queue.Dequeue();
                currentGroup.Add(sec);
                byte flags = sectionBytes[sec.x, sec.y, sec.z];

                if ((flags & MultiComponentBit) != 0)
                {
                    continue;
                }

                for (int i = 0; i < dirs.Length; i++)
                {
                    // Check neighbor connection flag (Bit i)
                    if ((flags & (1 << i)) == 0) continue;

                    Vector3Int n = sec + dirs[i];

                    if (n.x < 0 || n.y < 0 || n.z < 0 || n.x >= secW || n.y >= secH || n.z >= secD) continue;
                    if (visited[n.x, n.y, n.z]) continue;

                    // Reciprocal check: Ensure neighbor is also connected back
                    int oppositeDir = (i % 2 == 0) ? i + 1 : i - 1;
                    if ((sectionBytes[n.x, n.y, n.z] & (1 << oppositeDir)) == 0) continue;

                    visited[n.x, n.y, n.z] = true;
                    queue.Enqueue(n);
                }
            }
            allGroups.Add(currentGroup);
        }

        return allGroups;
    }

    private List<List<Vector3Int>> FindDisconnectedSectionGroups()
    {
        // Use the full section grid dimensions for the visited array.
        bool[,,] visited = new bool[secW, secH, secD];
        List<List<Vector3Int>> allGroups = new List<List<Vector3Int>>();

        // Iterate over every possible section in the chunk.
        for (int x = 0; x < secW; x++)
        {
            for (int y = 0; y < secH; y++)
            {
                for (int z = 0; z < secD; z++)
                {
                    Vector3Int startSec = new Vector3Int(x, y, z);

                    // Skip if already visited or if the section contains no voxels.
                    if (visited[x, y, z] || !SectionHasVoxels(startSec))
                        continue;

                    // Found a new group, start BFS from this section.
                    List<Vector3Int> currentGroup = new List<Vector3Int>();
                    Queue<Vector3Int> queue = new Queue<Vector3Int>();

                    queue.Enqueue(startSec);
                    visited[x, y, z] = true;

                    while (queue.Count > 0)
                    {
                        Vector3Int sec = queue.Dequeue();
                        currentGroup.Add(sec);
                        byte flags = sectionBytes[sec.x, sec.y, sec.z];

                        if ((flags & MultiComponentBit) != 0)
                        {
                            continue; // Stop BFS from exiting this section
                        }

                        for (int i = 0; i < dirs.Length; i++)
                        {
                            // Check neighbor connection flag (Bit i)
                            if ((flags & (1 << i)) == 0) continue;

                            Vector3Int n = sec + dirs[i];

                            // Neighbor bounds check (implicitly covered by loop limits if iterating all)
                            if (n.x < 0 || n.y < 0 || n.z < 0 || n.x >= secW || n.y >= secH || n.z >= secD) continue;
                            if (visited[n.x, n.y, n.z]) continue;

                            // Reciprocal check: Ensure neighbor is also connected back
                            int oppositeDir = (i % 2 == 0) ? i + 1 : i - 1;
                            if ((sectionBytes[n.x, n.y, n.z] & (1 << oppositeDir)) == 0) continue;

                            visited[n.x, n.y, n.z] = true;
                            queue.Enqueue(n);
                        }
                    }
                    allGroups.Add(currentGroup);
                }
            }
        }

        return allGroups;
    }

    // ------------------------------------------------------------------
    // IV. MAIN EDIT AND SPLIT FUNCTION (The entry point)
    // ------------------------------------------------------------------

    public void DestroyVoxelsInSphere(Vector3Int center, float radius)
    {
        int intRadius = Mathf.CeilToInt(radius);
        HashSet<Vector3Int> affectedSections = new HashSet<Vector3Int>();
        bool voxelsRemoved = false;

        // Collect voxels to remove, split by a random plane for debris
        List<Vector3Int> sideA = new List<Vector3Int>();
        List<Vector3Int> sideB = new List<Vector3Int>();
        Vector3 planeNormal = UnityEngine.Random.onUnitSphere;
        Vector3 centerF = new Vector3(center.x, center.y, center.z);

        for (int x = -intRadius; x <= intRadius; x++)
            for (int y = -intRadius; y <= intRadius; y++)
                for (int z = -intRadius; z <= intRadius; z++)
                {
                    Vector3Int pos = center + new Vector3Int(x, y, z);
                    if (pos.x < 0 || pos.y < 0 || pos.z < 0 || pos.x >= ChunkWidth || pos.y >= ChunkHeight || pos.z >= ChunkDepth) continue;
                    if (Vector3.Distance(center, pos) > radius) continue;
                    if (Blocks[pos.x, pos.y, pos.z] == 0) continue;

                    float side = Vector3.Dot(new Vector3(pos.x, pos.y, pos.z) - centerF, planeNormal);
                    if (side >= 0f) sideA.Add(pos); else sideB.Add(pos);
                }

        if (sideA.Count == 0 && sideB.Count == 0) return;

        // Spawn the two debris halves before zeroing so they read the correct block colors
        Vector3 worldCenter = transform.TransformPoint(centerF);
        if (sideA.Count > 0) SpawnDebrisChunk(sideA, worldCenter, planeNormal);
        if (sideB.Count > 0) SpawnDebrisChunk(sideB, worldCenter, -planeNormal);

        // Remove voxels and track affected sections
        foreach (var pos in sideA)
        {
            Blocks[pos.x, pos.y, pos.z] = 0;
            SetVoxelColor(pos.x, pos.y, pos.z, 0);
            voxelsRemoved = true;
            affectedSections.Add(GetSectionIndex(pos.x, pos.y, pos.z));
        }
        foreach (var pos in sideB)
        {
            Blocks[pos.x, pos.y, pos.z] = 0;
            SetVoxelColor(pos.x, pos.y, pos.z, 0);
            voxelsRemoved = true;
            affectedSections.Add(GetSectionIndex(pos.x, pos.y, pos.z));
        }

        if (!voxelsRemoved) return;

        // Apply the changes to the GPU
        VoxelTexture3D.Apply();

        // Identify sections needing neighbor flag updates (affected sections + their neighbors)
        HashSet<Vector3Int> sectionsToUpdate = new HashSet<Vector3Int>(affectedSections);
        Vector3Int[] dirs = { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
        foreach (var sec in affectedSections)
            foreach (var dir in dirs) sectionsToUpdate.Add(sec + dir);

        // Update neighbor flags (connection check)
        foreach (var sec in sectionsToUpdate)
            CalculateSectionNeighbors(sec, true);

        // FIND DISCONNECTED SECTION GROUPS (Localized, Fast check)
        List<List<Vector3Int>> allGroups = FindDisconnectedSectionGroups(sectionsToUpdate);

        if (allGroups.Count <= 1)
        {
            RebuildMesh();
            return;
        }

        //// Sort groups by size (descending) so the largest group remains untouched in the current chunk.
        //allGroups.Sort((a, b) => b.Count.CompareTo(a.Count));

        //// The largest component is allGroups[0], which we skip.
        //for (int i = 1; i < allGroups.Count; i++)
        //{
        //    // Pass the list of sections and the index (i) for naming
        //    SplitSectionGroup(allGroups[i], i);
        //}

        //// Apply the changes to the GPU
        //VoxelTexture3D.Apply();

        SplitDisconnectedPieces();

        // Rebuild the mesh for this chunk (which now only contains the absolute largest piece)
        RebuildMesh();
    }

    private void SplitSectionGroup(List<Vector3Int> sectionGroup, int groupIndex)
    {
        // Collect all non-zero voxels that belong to this section group.
        List<Vector3Int> voxelsToProcess = new List<Vector3Int>();
        foreach (var sec in sectionGroup)
        {
            int startX = sec.x * SectionSize;
            int startY = sec.y * SectionSize;
            int startZ = sec.z * SectionSize;
            int endX = Mathf.Min(startX + SectionSize, ChunkWidth);
            int endY = Mathf.Min(startY + SectionSize, ChunkHeight);
            int endZ = Mathf.Min(startZ + SectionSize, ChunkDepth);

            for (int x = startX; x < endX; x++)
                for (int y = startY; y < endY; y++)
                    for (int z = startZ; z < endZ; z++)
                        if (Blocks[x, y, z] != 0)
                            voxelsToProcess.Add(new Vector3Int(x, y, z));
        }

        if (voxelsToProcess.Count == 0) return;

        // Perform Voxel-level Connected Components Analysis (CCA) for groups > 1 voxel.
        Dictionary<Vector3Int, int> voxelLabels = new Dictionary<Vector3Int, int>();
        Dictionary<int, List<Vector3Int>> components = new Dictionary<int, List<Vector3Int>>();
        int currentLabel = 1;

        foreach (var startVoxel in voxelsToProcess)
        {
            if (voxelLabels.ContainsKey(startVoxel)) continue;

            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            queue.Enqueue(startVoxel);
            voxelLabels[startVoxel] = currentLabel;

            List<Vector3Int> componentVoxels = new List<Vector3Int> { startVoxel };

            while (queue.Count > 0)
            {
                Vector3Int v = queue.Dequeue();
                for (int i = 0; i < 6; i++)
                {
                    int nx = v.x + neighborOffsetX[i];
                    int ny = v.y + neighborOffsetY[i];
                    int nz = v.z + neighborOffsetZ[i];
                    Vector3Int n = new Vector3Int(nx, ny, nz);

                    // Check bounds within the main chunk
                    if (nx < 0 || nx >= ChunkWidth || ny < 0 || ny >= ChunkHeight || nz < 0 || nz >= ChunkDepth) continue;

                    // Check if it's a block AND it hasn't been labeled yet
                    if (Blocks[nx, ny, nz] == 0 || voxelLabels.ContainsKey(n)) continue;

                    // Crucial Constraint: Ensure the neighbor belongs to one of the sections in this 'sectionGroup'
                    if (!sectionGroup.Contains(GetSectionIndex(nx, ny, nz))) continue;

                    voxelLabels[n] = currentLabel;
                    queue.Enqueue(n);
                    componentVoxels.Add(n);
                }
            }

            components[currentLabel] = componentVoxels;
            currentLabel++;
        }

        // Create a new chunk for *each* found component.
        CreateNewChunksFromComponents(components, groupIndex);
    }

    private void CreateNewChunksFromComponents(Dictionary<int, List<Vector3Int>> voxelComponents, int groupIndex)
    {
        int pieceCounter = 1;
        foreach (var kvp in voxelComponents)
        {
            List<Vector3Int> voxels = kvp.Value;

            // Determine local bounds
            int minX = voxels.Min(v => v.x);
            int minY = voxels.Min(v => v.y);
            int minZ = voxels.Min(v => v.z);
            int maxX = voxels.Max(v => v.x);
            int maxY = voxels.Max(v => v.y);
            int maxZ = voxels.Max(v => v.z);

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            int depth = maxZ - minZ + 1;

            // Create new Blocks array
            byte[,,] newBlocks = new byte[width, height, depth];
            foreach (var v in voxels)
            {
                int lx = v.x - minX;
                int ly = v.y - minY;
                int lz = v.z - minZ;

                newBlocks[lx, ly, lz] = Blocks[v.x, v.y, v.z];

                // Remove from original chunk
                Blocks[v.x, v.y, v.z] = 0;
                SetVoxelColor(v.x, v.y, v.z, 0);
            }

            // Skip small chunks
            if (newBlocks.Length <= 4) continue;

            // Create and initialize the new chunk GameObject.

            Vector3 localOffset = new Vector3(minX, minY, minZ);
            Vector3 worldPos = transform.TransformPoint(localOffset);

            GameObject newGO = new GameObject($"{name}_Piece_G{groupIndex}_P{pieceCounter}");
            newGO.transform.position = worldPos;
            newGO.transform.rotation = transform.rotation;
            newGO.transform.localScale = transform.localScale;

            VoxelChunk newChunk = newGO.AddComponent<VoxelChunk>();
            newChunk.ChunkWidth = width;
            newChunk.ChunkHeight = height;
            newChunk.ChunkDepth = depth;
            newChunk.Blocks = newBlocks;
            newChunk.palette = palette;
            newChunk.MeshFilter = newGO.AddComponent<MeshFilter>();

            var renderer = newGO.AddComponent<MeshRenderer>();
            renderer.material = GetComponent<MeshRenderer>().material;

            // Rebuild mesh and initialize sections for the new chunk
            newChunk.InitializeSections();
            newChunk.RebuildMesh();

            pieceCounter++;
        }
    }

    // ------------------------------------------------------------------
    // V. DEBRIS SPAWNING
    // ------------------------------------------------------------------

    private void SpawnDebrisChunk(List<Vector3Int> voxels, Vector3 worldExplosionCenter, Vector3 impulseDir)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

        foreach (var v in voxels)
        {
            if (v.x < minX) minX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.z < minZ) minZ = v.z;
            if (v.x > maxX) maxX = v.x;
            if (v.y > maxY) maxY = v.y;
            if (v.z > maxZ) maxZ = v.z;
        }

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int depth = maxZ - minZ + 1;

        byte[,,] newBlocks = new byte[width, height, depth];
        foreach (var v in voxels)
            newBlocks[v.x - minX, v.y - minY, v.z - minZ] = Blocks[v.x, v.y, v.z];

        Vector3 worldPos = transform.TransformPoint(new Vector3(minX, minY, minZ));

        GameObject debrisGO = new GameObject($"{name}_Debris");
        debrisGO.transform.position = worldPos;
        debrisGO.transform.rotation = transform.rotation;
        debrisGO.transform.localScale = transform.localScale;

        VoxelChunk debrisChunk = debrisGO.AddComponent<VoxelChunk>();
        debrisChunk.ChunkWidth = width;
        debrisChunk.ChunkHeight = height;
        debrisChunk.ChunkDepth = depth;
        debrisChunk.Blocks = newBlocks;
        debrisChunk.palette = palette;
        debrisChunk.debrisForce = debrisForce;
        debrisChunk.debrisTorque = debrisTorque;
        debrisChunk.MeshFilter = debrisGO.AddComponent<MeshFilter>();

        var rend = debrisGO.AddComponent<MeshRenderer>();
        rend.material = GetComponent<MeshRenderer>().material;

        debrisChunk.InitializeSections();
        debrisChunk.RebuildMesh();

        Rigidbody debrisRb = debrisGO.GetComponent<Rigidbody>();
        if (debrisRb != null)
        {
            debrisRb.AddForce(impulseDir.normalized * debrisForce, ForceMode.Impulse);
            debrisRb.AddTorque(UnityEngine.Random.insideUnitSphere * debrisTorque, ForceMode.Impulse);
            //debrisRb.isKinematic = true;
            if (rb != null)
                rb.isKinematic = true;
        }
    }

    // ------------------------------------------------------------------
    // VI. DEBUG VISUALIZATION
    // ------------------------------------------------------------------

    //private void OnDrawGizmos()
    //{
    //    // Only draw if the chunk is active and has data and gizmos are enabled in the editor
    //    if (Blocks == null || secW == 0 || !enabled || !gameObject.activeInHierarchy)
    //        return;

    //    DrawSectionStateGizmos();
    //}

    //private void DrawSectionStateGizmos()
    //{
    //    Vector3 sectionLocalSize = new Vector3(SectionSize, SectionSize, SectionSize);
    //    Vector3 halfSectionLocalSize = sectionLocalSize / 2f;

    //    // Define direction vectors for drawing connections (relative to section center, scaled to half the section size)
    //    // Order matches bit flags: +x, -x, +y, -y, +z, -z
    //    Vector3[] localDirs = {
    //        Vector3.right * SectionSize / 2f,  // +x (Bit 0)
    //        Vector3.left * SectionSize / 2f,   // -x (Bit 1)
    //        Vector3.up * SectionSize / 2f,     // +y (Bit 2)
    //        Vector3.down * SectionSize / 2f,   // -y (Bit 3)
    //        Vector3.forward * SectionSize / 2f, // +z (Bit 4)
    //        Vector3.back * SectionSize / 2f    // -z (Bit 5)
    //    };

    //    for (int x = 0; x < secW; x++)
    //        for (int y = 0; y < secH; y++)
    //            for (int z = 0; z < secD; z++)
    //            {
    //                Vector3Int sec = new Vector3Int(x, y, z);
    //                // Read the connection flags from the byte array
    //                byte flags = sectionBytes[x, y, z];

    //                // Calculate the LOCAL center of the section (in chunk's local space)
    //                Vector3 localSectionCenter = new Vector3(
    //                    x * SectionSize + halfSectionLocalSize.x,
    //                    y * SectionSize + halfSectionLocalSize.y,
    //                    z * SectionSize + halfSectionLocalSize.z
    //                );

    //                // Visualize Section Box
    //                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.localScale);

    //                if (SectionHasVoxels(sec))
    //                {
    //                    // Active sections with voxels
    //                    Gizmos.color = new UnityEngine.Color(0.2f, 0.8f, 0.2f, 0.15f);
    //                    Gizmos.DrawCube(localSectionCenter, sectionLocalSize);
    //                }
    //                else
    //                {
    //                    // Empty sections
    //                    //Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.05f);
    //                    //Gizmos.DrawCube(localSectionCenter, sectionLocalSize);
    //                }

    //                // Reset Gizmos.matrix to identity for drawing lines in world space
    //                Gizmos.matrix = Matrix4x4.identity;

    //                // Convert local center to WORLD space for drawing connection lines
    //                Vector3 worldSectionCenter = transform.TransformPoint(localSectionCenter);


    //                // Visualize Section Connections
    //                if (SectionHasVoxels(sec))
    //                {
    //                    for (int i = 0; i < 6; i++)
    //                    {
    //                        // Convert local direction vector to world space, scaled by chunk's local scale
    //                        Vector3 worldDir = transform.TransformVector(localDirs[i]);

    //                        // Check if the connection flag (Bit i) is set
    //                        if ((flags & (1 << i)) != 0)
    //                        {
    //                            // Connection is active (Bit 0-5 = 1)
    //                            Gizmos.color = UnityEngine.Color.yellow;
    //                            Gizmos.DrawLine(worldSectionCenter, worldSectionCenter + worldDir);
    //                        }
    //                        else
    //                        {
    //                            // Connection is broken (Bit 0-5 = 0)
    //                            Gizmos.color = UnityEngine.Color.red;
    //                            Gizmos.DrawRay(worldSectionCenter, worldDir * 0.5f);
    //                        }
    //                    }
    //                }
    //            }
    //}
}

public static class VoxReaderExtensions
{
    public static UnityEngine.Color ToUnityColor(this VoxReader.Color voxelColor)
    {
        // Convert the RGBA values from [0, 255] to Unity's [0, 1] range
        return new UnityEngine.Color(
            voxelColor.R / 255f,
            voxelColor.G / 255f,
            voxelColor.B / 255f,
            voxelColor.A / 255f);
    }
}
