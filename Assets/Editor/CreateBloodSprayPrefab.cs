using UnityEngine;
using UnityEditor;

/// <summary>
/// Creates the BloodSprayParticle prefab. Use menu: Tools > Create Blood Spray Particle Prefab
/// </summary>
public static class CreateBloodSprayPrefab
{
    [MenuItem("Tools/Create Blood Spray Particle Prefab")]
    public static void Create()
    {
        GameObject go = new GameObject("BloodSprayParticle");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 0f;
        main.startSize = 0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        var emission = ps.emission;
        emission.enabled = false;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 0;

        string path = "Assets/Blood/BloodSprayParticle.prefab";
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();
        Debug.Log($"Created {path}");
    }
}
