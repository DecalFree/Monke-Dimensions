﻿#if EDITOR

#else
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using Monke_Dimensions.Interaction;
using Monke_Dimensions.Models;
using System.Threading.Tasks;
using HarmonyLib;
using Monke_Dimensions.Editor;
using Monke_Dimensions.Browser;

namespace Monke_Dimensions.Behaviours;

internal class DimensionManager : MonoBehaviour
{
    public static DimensionManager Instance { get; private set; }

    public DimensionPackage currentViewingPackage;
    public DimensionPackage currentDimensionPackage;

    internal GameObject loadedDimensionObj;

    private Dictionary<string, DimensionPackage> dimensions = new Dictionary<string, DimensionPackage>();
    private Dictionary<string, AssetBundle> loadedAssetBundles = new Dictionary<string, AssetBundle>();

    public GameObject[] currentLoadedDimensionObjects;

    public int currentPage = 1;
    public List<string> dimensionNames;

    public bool inDimension;

    public GameObject Garfield;

    internal DimensionManager()
    {
        Instance = this;
        loadedDimensionObj = new GameObject("LoadedDimension");
        ButtonSetup();
        LoadDimensions();
    }

    public void LoadDimensions()
    {
        Garfield = GameObject.Find("Garfield");
        Garfield.SetActive(false);
#if DEBUG
        Debug.Log("-> Found Dimension(s): <-");
#endif
        // for my pookie bear decal(free)
        string path = Path.Combine(Path.GetDirectoryName(typeof(Main).Assembly.Location), "Dimensions");
        string[] dimensionFiles = Directory.GetFiles(path, "*.dimension");

        dimensionNames = new List<string>();

        foreach (string dimensionFile in dimensionFiles)
        {
            string dimensionName = Path.GetFileNameWithoutExtension(dimensionFile);

            dimensionNames.Add(dimensionName);
            LoadDimension(dimensionFile);
        }
    }

    private void ButtonSetup()
    {
        Comps.LeftBtn.AddComponent<Button>().BtnType = ButtonType.Left;
        Comps.RightBtn.AddComponent<Button>().BtnType = ButtonType.Right;
        Comps.LoadBtn.AddComponent<Button>().BtnType = ButtonType.Load;
        Comps.BrowserButton.AddComponent<Button>().BtnType = ButtonType.Browser;
        Comps.GarfieldButton.AddComponent<Button>().BtnType = ButtonType.Garfield;
    }

    private void LoadDimension(string dimensionFile)
    {
        string currentPath = Path.GetFullPath(dimensionFile);

        using (var zip = ZipFile.OpenRead(currentPath))
        {
            ZipArchiveEntry packageEntry = zip.GetEntry("Package.json");

            if (packageEntry == null)
            {
                Debug.LogError("Invalid dimension: " + currentPath);
                return;
            }

            using (StreamReader packageReader = new StreamReader(packageEntry.Open()))
            {
                DimensionPackage package = Newtonsoft.Json.JsonConvert.DeserializeObject<DimensionPackage>(packageReader.ReadToEnd());
                string jsonContent = packageReader.ReadToEnd();

                Debug.Log($"[Monke Dimensions Map] -> Name: {package.Name}, Author: {package.Author}");
                dimensions.Add(dimensionFile, package);
            }

        }
    }

    private async void LoadAssets(string zipFilePath, string assetBundleName)
    {
        if (loadedAssetBundles.TryGetValue(assetBundleName, out AssetBundle cachedAssetBundle))
        {
            InstantiateDimensions(cachedAssetBundle);
            return;
        }

        byte[] zipBytes = File.ReadAllBytes(zipFilePath);
        byte[] bundleBytes;

        using (MemoryStream zipStream = new MemoryStream(zipBytes))
        using (ZipArchive zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            ZipArchiveEntry selectedEntry = zipArchive.Entries.FirstOrDefault(entry => string.IsNullOrEmpty(Path.GetExtension(entry.Name)));

            using (Stream entryStream = selectedEntry.Open())
            using (MemoryStream bundleStream = new MemoryStream())
            {
                await entryStream.CopyToAsync(bundleStream);
                bundleBytes = bundleStream.ToArray();
            }
        }

        TaskCompletionSource<AssetBundle> tcs = new TaskCompletionSource<AssetBundle>();

        AssetBundleCreateRequest assetBundleCreateRequest = AssetBundle.LoadFromMemoryAsync(bundleBytes);

        assetBundleCreateRequest.completed += operation =>
        {
            AssetBundle assetBundle = assetBundleCreateRequest.assetBundle;

            if (!loadedAssetBundles.ContainsKey(assetBundleName))
            {
                loadedAssetBundles[assetBundleName] = assetBundle;
            }
            else
            {
                assetBundle.Unload(true);
                assetBundle = loadedAssetBundles[assetBundleName];
            }

            tcs.SetResult(assetBundle);
        };

        AssetBundle loadedAssetBundle = await tcs.Task;

        InstantiateDimensions(loadedAssetBundle);
    }

    private void InstantiateDimensions(AssetBundle assetBundle)
    {
        currentLoadedDimensionObjects = assetBundle.LoadAllAssets<GameObject>();

        foreach (GameObject loadedObject in currentLoadedDimensionObjects)
        {
            GameObject instantiatedObject = Instantiate(loadedObject, loadedDimensionObj.transform);
            SetupSurface(instantiatedObject);
        }

        GameObject.Find(currentLoadedDimensionObjects.First().gameObject.name + "(Clone)").transform.position = new Vector3(0f, 0, 0f);

        loadedDimensionObj.transform.position = new Vector3(0f, 650f, 0f);
        Camera.main.farClipPlane += 25000;
        TeleportDimension.OnTeleport(currentDimensionPackage);
    }

    private void SetupSurface(GameObject obj)
    {
        if (obj.GetComponent<GorillaSurfaceOverride>() == null) obj.AddComponent<GorillaSurfaceOverride>();

        if (obj.GetComponent<Animation>() != null && obj.GetComponent<MovingPlatform>() == null) obj.AddComponent<MovingPlatform>();

        if (obj.transform.childCount > 0)
        {
            foreach (Transform child in obj.transform)
            {
                SetupSurface(child.gameObject);
            }
        }
    }

    public void LoadSelectedDimension(string dimensionName)
    {
        ZoneManagement zoneManager = FindObjectOfType<ZoneManagement>();
        ZoneData FindZoneData(GTZone zone) => (ZoneData)AccessTools.Method(typeof(ZoneManagement), "GetZoneData").Invoke(zoneManager, new object[] { zone });
        if (inDimension)
        {
            foreach (GameObject EnviormentObject in Comps.EnviormentObjects) EnviormentObject.SetActive(inDimension);

            FindZoneData(GTZone.forest).rootGameObjects[1].SetActive(inDimension);
            TeleportDimension.ReturnToMonke(currentDimensionPackage);
            UnloadCurrentDimension();

            return;
        }
        else
        {

            string dimensionFilePath = Path.Combine(BepInEx.Paths.PluginPath, "Dimensions", $"{dimensionName}.dimension");
            if (!File.Exists(dimensionFilePath))
            {
                dimensionFilePath = Path.Combine(Path.GetDirectoryName(typeof(DimensionManager).Assembly.Location), "Dimensions", $"{dimensionName}.dimension");
            }

            currentDimensionPackage = currentViewingPackage;
            LoadAssets(dimensionFilePath, currentDimensionPackage.Name);
            inDimension = true;
            foreach (GameObject EnviormentObject in Comps.EnviormentObjects) EnviormentObject.SetActive(false);
            FindZoneData(GTZone.forest).rootGameObjects[1].SetActive(false);
        }
    }

    public void UnloadCurrentDimension()
    {
        foreach (Transform child in loadedDimensionObj.transform)
        {
            Destroy(child.gameObject);
        }
        currentLoadedDimensionObjects = null;
        inDimension = false;
        loadedDimensionObj.transform.position = new Vector3(0f, 0f, 0f);
        Camera.main.farClipPlane -= 25000f;
    }

    public void SwitchPage(int direction)
    {
        int totalPages = dimensionNames.Count;

        currentPage = (currentPage + direction + totalPages) % totalPages;

        if (currentPage >= 0 && currentPage < dimensions.Count)
        {
            DimensionPackage selectedDimension = dimensions.Values.ElementAt(currentPage);

            Comps.AuthorText.text = $"AUTHOR: {selectedDimension.Author}".ToUpper();
            Comps.NameText.text = $"DIMENSION: {selectedDimension.Name}".ToUpper();
            Comps.DescriptionText.text = selectedDimension.Description.ToUpper();
            Comps.StatusText.text = $"DIMENSIONS FOUND: ({currentPage + 1} / {totalPages})";
            currentViewingPackage = selectedDimension;
        }
    }

    public void LoadDownloadedDimension()
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(DimensionManager).Assembly.Location), "Dimensions");
        var dimensionFiles = Directory.GetFiles(path, "*.dimension");

        foreach (string dimensionFile in dimensionFiles)
        {
            if (!dimensions.ContainsKey(dimensionFile))
            {
                string dimensionName = Path.GetFileNameWithoutExtension(dimensionFile);
                dimensionNames.Add(dimensionName);
                LoadDimension(dimensionFile);
            }
        }
    }

#if DEBUG
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 200, 500));

        GUILayout.Label("Available Dimensions:");

        foreach (string dimensionName in dimensionNames)
        {
            if (GUILayout.Button(dimensionName))
            {
                LoadSelectedDimension(dimensionName);
            }
        }

        GUILayout.Space(20);

        GUILayout.Label("Page Navigation:");

        if (GUILayout.Button("Previous Page"))
        {
            SwitchPage(-1);
        }

        if (GUILayout.Button("Next Page"))
        {
            SwitchPage(1);
        }

        GUILayout.Space(20);

        GUILayout.Label($"Current Page: {currentPage + 1}");

        GUILayout.Label("Load Current Dimension:");

        if (GUILayout.Button("Load Current Dimension"))
        {
            LoadSelectedDimension(dimensionNames[currentPage]);
        }
        GUILayout.Space(20);

        if (GUILayout.Button("Join Crafterbot"))
        {
            Utilla.Utils.RoomUtils.JoinPrivateLobby("CRAFTERBOT");
        }
        GUILayout.Space(30);
        GUILayout.Label("Browser Stuff");
        GUILayout.Space(10);

        if (GUILayout.Button("Open Browser"))
        {
            DimensionBrowser.inBrowser = true;
            DimensionBrowser.instance.OnBrowserEnabled();
        }
        if (GUILayout.Button("Next Page"))
        {
            DimensionBrowser.instance.NextPage();
        }
        if (GUILayout.Button("Previous Page"))
        {
            DimensionBrowser.instance.PreviousPage();
        }

        GUILayout.EndArea();
    }
#endif
}
#endif