using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Windows;
using Random = System.Random;

public class DLATerrainGenerator : MonoBehaviour
{
    [SerializeField] private ComputeShader DLAComputeShader;
    
    [SerializeField] private int resolutionTarget = 512;
    [SerializeField] private int startSize = 8;

    [SerializeField] private int blurAmount = 3;
    [SerializeField] private int smoothingIterations = 2;
    [SerializeField] private int smoothAmount = 2;
    
    [SerializeField] [Range(0, 0.7f)] private float fillThreshold = 0.2f;
    
    [SerializeField] private int peaks = 4;
    [SerializeField] private AnimationCurve ridgeFalloffCurve;
    [SerializeField] private float falloffMax = 20; // effectively correlates to range scale
    [SerializeField] private float heightScale = 15;
    
    [SerializeField] private bool randomSeed;
    [SerializeField] private int seed;

    public OutputMode outputMode = OutputMode.Mesh;
    
    [HideInInspector] public float meshScale = 10;
    [HideInInspector] public MeshFilter meshFilter; // change to getcomponent

    [HideInInspector] public Terrain terrain;

    [HideInInspector] public string heightmapFileLocation;

    private List<Node> _rootNodes;
    private Node[,] _map;
    private Texture2D/*float[,]*/ _heightmap;
    private int _currentSize;

    private int _pixelsFilled;
    private Random _random; // might use unity static random maybe
    
    private static readonly int ShaderInputTexID = Shader.PropertyToID("Input");
    private static readonly int ShaderOutputTexID = Shader.PropertyToID("Output");
    private static readonly int BlurSizeID = Shader.PropertyToID("BlurSize");

    public void GenerateTerrain()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        
        // initialization
        _map = new Node[startSize, startSize];
        _heightmap = new Texture2D(startSize, startSize, TextureFormat.RFloat, false);//float[startSize, startSize];
        
        _currentSize = startSize;

        if (randomSeed)
            seed = new Random().Next();
       
        _random = new Random(seed);
        
        // peaks / root nodes
        _rootNodes = new List<Node>();
        _pixelsFilled = 0;
        for (int i = 0; i < peaks; i++)
        {
            int x = _random.Next(0, _currentSize);
            int y = _random.Next(0, _currentSize);
        
            if (_map[x, y] != null) continue;
            
            Node start = new Node(x, y);
            _map[x, y] = start;
            _rootNodes.Add(start);
            _pixelsFilled++;
        }

        // main generation
        while (_currentSize < resolutionTarget)
        {
            // add new nodes until some amount full
            while (_pixelsFilled <= _currentSize * _currentSize * fillThreshold)
                AddPixel();
            
            // upscale both maps and blur
            _currentSize *= 2;
            _pixelsFilled *= 2;
            
            UpscaleDetailed();
            
            UpdateAndScaleHeightmap();
        }
        
        // one final pass
        while (_pixelsFilled <= _currentSize * _currentSize * fillThreshold)
            AddPixel();
        
        foreach (Node node in _rootNodes)
            AddDetailedWithFalloff(node);
        
        _heightmap.Apply();

        for (int i = 0; i < smoothingIterations; i++)
            _heightmap = Blur(_heightmap, smoothAmount);
        
        watch.Stop();
        Debug.Log($"Generated Terrain Heightmap in {watch.Elapsed.TotalMilliseconds}ms");


        switch (outputMode)
        {
            case OutputMode.Mesh:
                GenerateMesh();
                break;
            case OutputMode.Terrain:
                ApplyToTerrain();
                break;
            case OutputMode.Heightmap:
                SaveGrayscaleImage(_heightmap, heightmapFileLocation);
                break;
        }
        
    }
    
    private void AddPixel()
    {
        // start in an empty space
        int x, y;
        do
        {
            x = _random.Next(0, _currentSize);
            y = _random.Next(0, _currentSize);
        } while (_map[x, y] != null);
        
        // move until it hits something
        while (true)
        {
            int axis = _random.Next(0, 2);
            int dx = axis == 0 ? (_random.Next(0, 2) == 0 ? -1 : 1) : 0;
            int dy = axis == 1 ? (_random.Next(0, 2) == 0 ? -1 : 1) : 0;

            if (x + dx > _currentSize - 1 || x + dx < 0)
                dx = -dx;
            if (y + dy > _currentSize - 1 || y + dy < 0)
                dy = -dy;

            Node hitNode = _map[x + dx, y + dy];
            if (hitNode == null)
            {
                x += dx;
                y += dy;
                continue;
            }
            
            Node newNode = new Node(x, y);
            _map[x, y] = newNode;
            hitNode.Children.Add(newNode);

            _pixelsFilled++;
            return;
        }
        
    }
    
    private void UpscaleDetailed()
    {
        // current size is now updated
        _map = new Node[_currentSize, _currentSize];
        
        foreach (Node node in _rootNodes)
            UpscaleSingleDetailed(node);
    }

    private void UpscaleSingleDetailed(Node node)
    {
        node.Fx *= 2;
        // node.Fx += (float)_random.NextDouble() - 0.5f;
        node.Fy *= 2;
        // node.Fy += (float)_random.NextDouble() - 0.5f;
        _map[node.X, node.Y] = node;

        List<Node> oldChildren = node.Children;
        node.Children = new List<Node>();

        foreach (Node child in oldChildren)
        {
            // make a point in between
            float dx = (child.Fx * 2 - node.Fx) * 0.5f;
            float dy = (child.Fy * 2 - node.Fy) * 0.5f;

            // random jiggle
            float x = node.Fx + dx + ((float)_random.NextDouble() - 0.5f);
            float y = node.Fy + dy + ((float)_random.NextDouble() - 0.5f);

            x = Math.Clamp(x, 0, _currentSize - 1);
            y = Math.Clamp(y, 0, _currentSize - 1);

            Node newChild = new Node(x, y);
            _map[newChild.X, newChild.Y] = newChild;

            node.Children.Add(newChild);
            newChild.Children.Add(child);

            // do all this to the children (i sure love recursion)
            UpscaleSingleDetailed(child);
        }
        
    }
    
    private void AddUpscaleBlurred()
    {
        // convolution and linear interpolation upscale
        var watch = System.Diagnostics.Stopwatch.StartNew();
        
        _heightmap = LerpScale(_heightmap);
        _heightmap = Blur(_heightmap, blurAmount);
        
        watch.Stop();
        Debug.Log($"Completetd blur and lerpscale in {watch.Elapsed.TotalMilliseconds}ms");
    }
    
    private Texture2D LerpScale(Texture2D input)
    {

        return DispatchToShader(input, "Upscale");


        // float[,] output = new float[_currentSize, _currentSize];
        // int oldSize = _currentSize / 2;
        //
        // for (int y = 0; y < oldSize; y++)
        // for (int x = 0; x < oldSize; x++)
        // {
        //     float val = input[y, x];
        //     float right = (x + 1 < oldSize) ? input[y, x + 1] : val;
        //     float bottom = (y + 1 < oldSize) ? input[y + 1, x] : val;
        //     float bottomRight = (x + 1 < oldSize && y + 1 < oldSize) ? input[y + 1, x + 1] : val;
        //
        //     // Assign interpolated values
        //     output[y * 2, x * 2] = val;
        //     output[y * 2, x * 2 + 1] = (val + right) * 0.5f;
        //     output[y * 2 + 1, x * 2] = (val + bottom) * 0.5f;
        //     output[y * 2 + 1, x * 2 + 1] = (val + right + bottom + bottomRight) * 0.25f;
        // }
        //
        // return output;
    }

    private Texture2D DispatchToShader(Texture2D input, string kernelName)
    {
        RenderTexture output = new RenderTexture(_currentSize, _currentSize, 0, RenderTextureFormat.RFloat);
        output.enableRandomWrite = true;
        output.Create();
        
        int upscaleKernel = DLAComputeShader.FindKernel(kernelName); // should probably cache
        
        DLAComputeShader.SetTexture(upscaleKernel, ShaderInputTexID, input);
        DLAComputeShader.SetTexture(upscaleKernel, ShaderOutputTexID, output);
        
        int threadGroups = Mathf.CeilToInt(_currentSize * 0.25f);
        
        DLAComputeShader.Dispatch(upscaleKernel, threadGroups, threadGroups, 1);
        return ConvertRenderTextureToTexture2D(output);
    }
    
    Texture2D ConvertRenderTextureToTexture2D(RenderTexture rTex)
    {
        Texture2D tex = new Texture2D(rTex.width, rTex.height, TextureFormat.RGBA32, false);
        
        // Copy the RenderTexture to Texture2D
        RenderTexture.active = rTex;
        tex.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
        tex.Apply();

        RenderTexture.active = null;
        return tex;
    }
    
    private Texture2D Blur(Texture2D image, int blurSize)
    {
        blurSize = Math.Max(1, blurSize);
        DLAComputeShader.SetInt(BlurSizeID, blurSize);
        return DispatchToShader(image, "Blur");
        //
        // float[,] blurredImage = new float[_currentSize, _currentSize];
        //
        //
        // // Iterate through each pixel in the image
        // for (int y = 0; y < _currentSize; y++)
        // for (int x = 0; x < _currentSize; x++)
        // {
        //     float sum = 0;
        //     int count = 0;
        //
        //     // Iterate over the neighborhood defined by the blur radius
        //     for (int dy = -radius; dy <= radius; dy++)
        //     for (int dx = -radius; dx <= radius; dx++)
        //     {
        //         int nx = x + dx;
        //         int ny = y + dy;
        //
        //         // Check if the neighbor is within bounds
        //         if (nx < 0 || nx >= _currentSize || ny < 0 || ny >= _currentSize) continue;
        //         
        //         sum += image[ny, nx];
        //         count++;
        //     }
        //
        //     // Compute the average and assign the new blurred value
        //     blurredImage[y, x] = sum / count;
        // }
        //
        // return blurredImage;
    }

    private void UpdateAndScaleHeightmap()
    {
        AddUpscaleBlurred();
        
        foreach (Node node in _rootNodes)
            AddDetailedWithFalloff(node);
        
        _heightmap.Apply();
        
    }

    private int AddDetailedWithFalloff(Node node)
    {
        // recursion again!!
        int max = 0;
        foreach (Node child in node.Children)
        {
            int height = AddDetailedWithFalloff(child);
            if (height > max)
                max = height;
        }

        Color color = _heightmap.GetPixel(node.X, node.Y);
        color.r += heightScale * ridgeFalloffCurve.Evaluate((max + 1) / falloffMax);
        _heightmap.SetPixel(node.X, node.Y, color);
        // _heightmap[node.X, node.Y] += heightScale * ridgeFalloffCurve.Evaluate((max + 1) / falloffMax);
        
        
        return max + 1;
    }
    
   
    private void GenerateMesh() // maybe this could be done with compute shader
    {
        int width = _currentSize;
        int height = _currentSize;

        Vector3[] vertices = new Vector3[width * height];
        int[] triangles = new int[(width - 1) * (height - 1) * 6];
        Vector2[] uvs = new Vector2[vertices.Length];

        // Generate vertices and UVs
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                vertices[index] = new Vector3(x * meshScale, _heightmap.GetPixel(x, z).r, z * meshScale);
                uvs[index] = new Vector2((float)x / width, (float)z / height);
            }
        }

        // Generate triangles
        int triangleIndex = 0;
        for (int z = 0; z < height - 1; z++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int topLeft = z * width + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + width;
                int bottomRight = bottomLeft + 1;

                // First triangle
                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;

                // Second triangle
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = bottomRight;
            }
        }

        // Create mesh
        Mesh mesh = new Mesh();
        if (width * height > 65535)
            mesh.indexFormat = IndexFormat.UInt32; // Use 32-bit indices if needed

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        // Assign to MeshFilter
        meshFilter.mesh = mesh;
    }
    
    private void SaveGrayscaleImage(Texture2D pixelValues, string filePath)
    {
        int width = _currentSize;
        int height = _currentSize;

        // Find min and max values in the float array
        float minValue = float.MaxValue;
        float maxValue = float.MinValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = pixelValues.GetPixel(x, y).r;
                if (value < minValue) minValue = value;
                if (value > maxValue) maxValue = value;
            }
        }

        // Avoid division by zero if all values are the same
        float range = maxValue - minValue;
        if (range == 0) range = 1;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RFloat, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Normalize value between 0 and 1
                float normalizedValue = (pixelValues.GetPixel(x, y).r - minValue) / range;
                Color color = new Color(normalizedValue, normalizedValue, normalizedValue); // Grayscale
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();

        // Encode to PNG and save the file
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);

        Debug.Log($"Image saved to: {filePath}");

        // Clean up
        DestroyImmediate(texture);
    }
    
    private void ApplyToTerrain()
    {
        int width = _currentSize;
        int height = _currentSize;
        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = _currentSize + 1;

        float[,] heights = new float[width, height];
        float maxTerrainHeight = terrainData.size.y;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            heights[y, x] = _heightmap.GetPixel(x, y).r * 100 / maxTerrainHeight; // Normalize height
        
        terrainData.SetHeights(0, 0, heights);
    }
    
}
