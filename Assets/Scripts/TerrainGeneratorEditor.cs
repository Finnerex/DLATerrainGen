using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(DLATerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        DLATerrainGenerator t = (DLATerrainGenerator)target;

        switch (t.outputMode)
        {
            case OutputMode.Mesh:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("meshFilter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("meshScale"));
                break;
            case OutputMode.Terrain:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("terrain"));
                break;
            case OutputMode.Heightmap:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("heightmapFileLocation"));
                break;
        }

        serializedObject.ApplyModifiedProperties();
        
        if(GUILayout.Button("Generate!"))
            t.GenerateTerrain();
        
    }
    
}
#endif

public enum OutputMode
{
    Mesh,
    Terrain,
    Heightmap
}
