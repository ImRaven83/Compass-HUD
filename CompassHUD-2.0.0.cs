using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.Quests;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

// SPT 5.0 runs EFT 1.0 on IL2CPP, so this is a BepInEx 6 IL2CPP plugin: the plugin itself is a
// BasePlugin, Unity callbacks come from an injected MonoBehaviour, and all HUD state lives in the
// plain managed CompassHUD class so none of it has to cross into IL2CPP.
[BepInPlugin("com.vinarators.compasshud", "Compass HUD", "2.0.1")]
public class CompassHUDPlugin : BasePlugin
{
    public override void Load()
    {
        CompassHUD.Initialize(Config, Log);

        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<CompassHUDDriver>())
        {
            ClassInjector.RegisterTypeInIl2Cpp<CompassHUDDriver>();
        }
        AddComponent<CompassHUDDriver>();
    }
}

public class CompassHUDDriver : MonoBehaviour
{
    public CompassHUDDriver(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        CompassHUD.Instance?.Update();
    }

    public void OnGUI()
    {
        CompassHUD.Instance?.OnGUI();
    }
}

public class CompassHUD
{
    public enum MarkerType
    {
        Extraction,
        Transit,
        Quest
    }

    public class MarkerTarget
    {
        public Vector3 Position;
        public MarkerType Type;
        public float CurrentDistance;
        public string Name;
    }

    public static CompassHUD Instance { get; private set; }

    private readonly ManualLogSource Logger;
    private readonly ConfigFile Config;

    private GUIStyle compassStyle;
    private GUIStyle degreeStyle;
    private GUIStyle centralDegreeStyle;
    private GUIStyle distanceStyle;
    private GUIStyle subDistanceStyle;
    private GUIStyle bottomLabelStyle;
    private Texture2D lineTexture;
    private Texture2D leftGradient;
    private Texture2D rightGradient;
    private Texture2D extractionTexture;
    private Texture2D questTexture;
    private bool resourcesCreated;
    private bool isInRaid;
    private bool localeIsRussian;
    private float lastCheckTime;
    private float lastMarkerCheckTime;
    private float raidStartTime;
    private float cachedYaw;

    private ConfigEntry<bool> enabledCompass;
    private ConfigEntry<bool> showDegrees;
    private ConfigEntry<bool> showLetters;
    private ConfigEntry<float> scale;
    private ConfigEntry<float> opacity;
    private ConfigEntry<int> language;
    private ConfigEntry<KeyCode> toggleKey;
    private ConfigEntry<float> roundedness;
    private ConfigEntry<bool> showCentralIndicator;
    private ConfigEntry<bool> showExtractions;
    private ConfigEntry<bool> showTransits;
    private ConfigEntry<bool> showQuests;
    private ConfigEntry<bool> independentMarkerSize;
    private ConfigEntry<float> markerScale;
    private ConfigEntry<float> compassYPosition;
    private ConfigEntry<bool> labelsAboveTape;

    private List<MarkerTarget> activeMarkers = new List<MarkerTarget>();
    private List<ExfiltrationPoint> cachedExfils = new List<ExfiltrationPoint>();
    private List<TransitPoint> cachedTransits = new List<TransitPoint>();
    private List<TriggerWithId> cachedQuestTriggers = new List<TriggerWithId>();
    private List<float> currentFrameMarkerX = new List<float>();
    private bool markersInitialized;
    private bool questReadFailureLogged;
    private Player mainPlayer;
    private bool fatalError;

    private CompassHUD(ConfigFile config, ManualLogSource logger)
    {
        Config = config;
        Logger = logger;
    }

    public static void Initialize(ConfigFile config, ManualLogSource logger)
    {
        Instance = new CompassHUD(config, logger);
        Instance.BindConfig();
    }

    private void BindConfig()
    {
        enabledCompass = Config.Bind("General", "Enabled", true, "Enable Compass HUD");
        showDegrees = Config.Bind("General", "Show Degrees", true, "Show heading degrees on tape");
        showLetters = Config.Bind("General", "Show Letters", true, "Show heading letters on tape");
        language = Config.Bind("General", "Language", 0, "0 = Auto, 1 = English, 2 = Russian");
        scale = Config.Bind("Appearance", "Scale", 1.0f, new ConfigDescription("Compass scale", new AcceptableValueRange<float>(0.25f, 3.0f)));
        opacity = Config.Bind("Appearance", "Opacity", 0.9f, new ConfigDescription("Compass opacity", new AcceptableValueRange<float>(0.0f, 1.0f)));
        toggleKey = Config.Bind("General", "Toggle Hotkey", KeyCode.F8, "Button to toggle compass visibility");
        roundedness = Config.Bind("Appearance", "Roundedness", 1.0f, new ConfigDescription("How strongly the sides fade out for 3D effect", new AcceptableValueRange<float>(0.0f, 10.0f)));
        showCentralIndicator = Config.Bind("General", "Show Central Indicator", true, "Show the central indicator box");
        showExtractions = Config.Bind("UI Markers", "Show Extractions", false, "Show extraction markers on compass");
        showTransits = Config.Bind("UI Markers", "Show Transits", false, "Show map transit points on compass");
        showQuests = Config.Bind("UI Markers", "Show Quests", false, "Show quest targets on compass");
        independentMarkerSize = Config.Bind("UI Markers", "Independent Marker Size", false, "Disable distance-based scaling for markers");
        markerScale = Config.Bind("UI Markers", "Marker Scale", 1.21f, new ConfigDescription("Scale factor for markers and icons", new AcceptableValueRange<float>(0.25f, 3.0f)));
        compassYPosition = Config.Bind("Appearance", "Vertical Position", 0.0f, new ConfigDescription("Vertical compass position: 0% at the top, 100% at the bottom", new AcceptableValueRange<float>(0.0f, 1.0f)));
        labelsAboveTape = Config.Bind("Appearance", "Labels Above Tape", false, "Draw all compass text above the tape instead of below it, so the tape itself can sit flush against the bottom of the screen at 100% Vertical Position.");

        Logger.LogInfo($"Compass HUD config loaded: LabelsAboveTape={labelsAboveTape.Value}, VerticalPosition={compassYPosition.Value}, Scale={scale.Value}, ConfigFile={Config.ConfigFilePath}");
    }

    // Unity objects are created on the first frame rather than in Load, which runs before the engine is fully up.
    private void EnsureResources()
    {
        if (resourcesCreated) return;
        resourcesCreated = true;

        compassStyle = new GUIStyle();
        compassStyle.fontStyle = FontStyle.Bold;
        compassStyle.alignment = TextAnchor.MiddleCenter;
        compassStyle.normal.textColor = Color.white;

        degreeStyle = new GUIStyle();
        degreeStyle.fontStyle = FontStyle.Normal;
        degreeStyle.alignment = TextAnchor.MiddleCenter;
        degreeStyle.normal.textColor = Color.white;

        centralDegreeStyle = new GUIStyle();
        centralDegreeStyle.fontStyle = FontStyle.Bold;
        centralDegreeStyle.alignment = TextAnchor.MiddleCenter;
        centralDegreeStyle.normal.textColor = Color.white;

        distanceStyle = new GUIStyle();
        distanceStyle.fontStyle = FontStyle.Bold;
        distanceStyle.alignment = TextAnchor.MiddleCenter;
        distanceStyle.normal.textColor = Color.white;

        subDistanceStyle = new GUIStyle(distanceStyle);
        bottomLabelStyle = new GUIStyle(distanceStyle);

        lineTexture = new Texture2D(1, 1);
        lineTexture.SetPixel(0, 0, Color.white);
        lineTexture.Apply();
        KeepAlive(lineTexture);

        leftGradient = new Texture2D(32, 1);
        rightGradient = new Texture2D(32, 1);
        for (int i = 0; i < 32; i++)
        {
            float t = (float)i / 31f;
            leftGradient.SetPixel(i, 0, new Color(0f, 0f, 0f, 0.18f * t));
            rightGradient.SetPixel(i, 0, new Color(0f, 0f, 0f, 0.18f * (1f - t)));
        }
        leftGradient.Apply();
        rightGradient.Apply();
        KeepAlive(leftGradient);
        KeepAlive(rightGradient);

        extractionTexture = LoadTextureFromFile("Standard_27.png");
        questTexture = LoadTextureFromFile("icon_quest.png");
    }

    // Nothing on the IL2CPP side references our textures, so stop scene unloads from collecting them.
    private static void KeepAlive(Texture2D texture)
    {
        if (texture != null)
            texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }

    private Texture2D LoadTextureFromFile(string filename)
    {
        string[] possiblePaths = new string[]
        {
            Path.GetDirectoryName(typeof(CompassHUD).Assembly.Location) ?? global::BepInEx.Paths.PluginPath,
            Path.Combine(global::BepInEx.Paths.GameRootPath, "SPT_Runtime", "SPT_Data", "images"),
            Path.Combine(global::BepInEx.Paths.GameRootPath, "SPT", "SPT_Data"),
            Path.Combine(global::BepInEx.Paths.GameRootPath, "SPT_Data"),
            Path.Combine(global::BepInEx.Paths.GameRootPath, "..", "SPT", "SPT_Data"),
            Path.Combine(global::BepInEx.Paths.GameRootPath, "EscapeFromTarkov_Data"),
            global::BepInEx.Paths.GameRootPath
        };

        bool anyDirChecked = false;
        foreach (var basePath in possiblePaths)
        {
            if (!Directory.Exists(basePath)) continue;
            anyDirChecked = true;

            string[] files;
            try
            {
                files = Directory.GetFiles(basePath, filename, SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Compass HUD couldn't search {basePath} for {filename}: {ex.Message}");
                continue;
            }

            if (files.Length > 0)
            {
                byte[] fileData = File.ReadAllBytes(files[0]);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, fileData))
                {
                    KeepAlive(texture);
                    Logger.LogInfo($"Compass HUD loaded {filename} from {files[0]}");
                    return texture;
                }
                Logger.LogWarning($"Compass HUD found {filename} at {files[0]} but could not decode it as an image.");
            }
        }

        Logger.LogWarning($"Compass HUD could not find {filename} under any known SPT data folder (searched: {string.Join(", ", possiblePaths)}; any existed: {anyDirChecked}). Extraction/quest markers will fall back to plain colored squares.");
        return null;
    }

    private void LogFatal(string context, Exception ex)
    {
        fatalError = true;
        Logger.LogError($"Compass HUD disabled itself after an unexpected error in {context} (likely a game/API change). {ex}");
    }

    private static string Localize(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        try
        {
            return LocalizationExtensions.Localized(key, (string)null) ?? key;
        }
        catch
        {
            return key;
        }
    }

    private bool UseRussian => language.Value == 2 || (language.Value == 0 && localeIsRussian);

    private static GameWorld GetGameWorld()
    {
        try
        {
            return Singleton<GameWorld>.Instance;
        }
        catch
        {
            return null;
        }
    }

    public void Update()
    {
        if (fatalError)
            return;

        try
        {
            UpdateInternal();
        }
        catch (Exception ex)
        {
            LogFatal(nameof(Update), ex);
        }
    }

    private void UpdateInternal()
    {
        EnsureResources();

        if (Time.time - lastCheckTime > 0.5f)
        {
            lastCheckTime = Time.time;
            GameWorld world = GetGameWorld();
            isInRaid = world != null;

            if (!isInRaid)
            {
                mainPlayer = null;
                markersInitialized = false;
                cachedExfils.Clear();
                cachedTransits.Clear();
                cachedQuestTriggers.Clear();
                activeMarkers.Clear();
            }
            else if (mainPlayer == null)
            {
                try
                {
                    mainPlayer = world.MainPlayer;
                }
                catch { }

                if (mainPlayer == null)
                {
                    mainPlayer = UnityEngine.Object.FindObjectsOfType<Player>().FirstOrDefault(p => p != null && p.IsYourPlayer);
                }

                if (mainPlayer != null)
                {
                    raidStartTime = Time.time;
                    localeIsRussian = (Localize("Exit") ?? "").ToLower().Contains("выход");
                }
            }
        }

        if (!isInRaid || Cursor.visible)
        {
            return;
        }

        if (mainPlayer != null && !markersInitialized && (Time.time - raidStartTime > 4.0f))
        {
            ScanMarkers();
        }

        if (Input.GetKeyDown(toggleKey.Value))
        {
            enabledCompass.Value = !enabledCompass.Value;
        }

        if (!enabledCompass.Value)
            return;

        Camera cam = Camera.main;
        if (cam == null) cam = Camera.current;
        if (cam != null)
        {
            cachedYaw = Normalize(cam.transform.eulerAngles.y + 180f);
        }
        else if (mainPlayer != null && mainPlayer.CameraPosition != null)
        {
            cachedYaw = Normalize(mainPlayer.CameraPosition.eulerAngles.y + 180f);
        }

        if (Time.time - lastMarkerCheckTime > 1.0f)
        {
            lastMarkerCheckTime = Time.time;
            RefreshActiveTargets();
        }
    }

    private void ScanMarkers()
    {
        GameWorld world = GetGameWorld();
        cachedExfils = GetEligibleExfils(world);

        cachedTransits.Clear();
        try
        {
            foreach (var transit in UnityEngine.Object.FindObjectsOfType<TransitPoint>())
            {
                if (transit != null) cachedTransits.Add(transit);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Compass HUD failed to scan transit points: {ex.Message}");
        }

        cachedQuestTriggers.Clear();
        var triggerTypeCounts = new Dictionary<string, int>();
        if (showQuests.Value)
        {
            try
            {
                foreach (var trigger in UnityEngine.Object.FindObjectsOfType<TriggerWithId>())
                {
                    if (trigger == null) continue;
                    string typeName = trigger.GetIl2CppType().Name;
                    triggerTypeCounts.TryGetValue(typeName, out int count);
                    triggerTypeCounts[typeName] = count + 1;
                    if (!string.IsNullOrEmpty(trigger.Id))
                        cachedQuestTriggers.Add(trigger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Compass HUD failed to scan quest triggers: {ex.Message}");
            }
        }

        markersInitialized = true;
        RefreshActiveTargets();

        int questZoneCount = 0;
        try
        {
            questZoneCount = GetActiveQuestZones().Count;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Compass HUD failed to read active quests: {ex.Message}");
        }

        string triggerSummary = triggerTypeCounts.Count == 0 ? "none" : string.Join(", ", triggerTypeCounts.Select(kv => $"{kv.Key}={kv.Value}"));
        Logger.LogInfo($"Compass HUD marker scan: ShowExtractions={showExtractions.Value}, ShowTransits={showTransits.Value}, ShowQuests={showQuests.Value}, " +
            $"exfils={cachedExfils.Count}, transits={cachedTransits.Count}, " +
            $"triggers with ids={cachedQuestTriggers.Count} (by type: {triggerSummary}), active quest zones={questZoneCount}, questLoot={GetQuestLoot().Count}, " +
            $"activeMarkers after filtering: total={activeMarkers.Count}, extraction={activeMarkers.Count(m => m.Type == MarkerType.Extraction)}, " +
            $"transit={activeMarkers.Count(m => m.Type == MarkerType.Transit)}, quest={activeMarkers.Count(m => m.Type == MarkerType.Quest)}");
    }

    private string LocalizeAndCleanName(string rawName, string fallbackSearchText = "", bool isTransit = false)
    {
        if (string.IsNullOrEmpty(rawName)) rawName = fallbackSearchText;
        if (string.IsNullOrEmpty(rawName)) return "Exit";
        bool ru = localeIsRussian;

        string lower = rawName.ToLower() + " " + (fallbackSearchText ?? "").ToLower();
        if (isTransit || lower.Contains("transit") || lower.Contains("transfer") || lower.Contains("to_"))
        {
            if (lower.Contains("interchange") || lower.Contains("razvjazka")) return ru ? "Переход: Развязка" : "Transit: Interchange";
            if (lower.Contains("bigmap") || lower.Contains("customs") || lower.Contains("tamozhna")) return ru ? "Переход: Таможня" : "Transit: Customs";
            if (lower.Contains("shoreline") || lower.Contains("bereg")) return ru ? "Переход: Берег" : "Transit: Shoreline";
            if (lower.Contains("lighthouse") || lower.Contains("majak")) return ru ? "Переход: Маяк" : "Transit: Lighthouse";
            if (lower.Contains("rezerv") || lower.Contains("reserve")) return ru ? "Переход: Резерв" : "Transit: Reserve";
            if (lower.Contains("woods") || lower.Contains("les")) return ru ? "Переход: Лес" : "Transit: Woods";
            if (lower.Contains("town") || lower.Contains("streets") || lower.Contains("ulici") || lower.Contains("streets of tarkov")) return ru ? "Переход: Улицы Таркова" : "Transit: Streets";
            if (lower.Contains("sandbox") || lower.Contains("groundzero") || lower.Contains("epicentr") || lower.Contains("ground zero") || lower.Contains("epi")) return ru ? "Переход: Эпицентр" : "Transit: Ground Zero";
            if (lower.Contains("factory") || lower.Contains("zavod")) return ru ? "Переход: Завод" : "Transit: Factory";
            if (lower.Contains("terminal")) return ru ? "Переход: Терминал" : "Transit: Terminal";
            if (lower.Contains("lab")) return ru ? "Переход: Лаборатория" : "Transit: Laboratory";
            return ru ? "Переход" : "Transit Point";
        }

        string localized = Localize(rawName);
        if (!string.IsNullOrEmpty(localized) && localized != rawName)
        {
            return localized;
        }

        string upperLocalized = Localize(rawName.ToUpper());
        if (!string.IsNullOrEmpty(upperLocalized) && upperLocalized != rawName.ToUpper())
        {
            return upperLocalized;
        }

        if (lower.Contains("unity_free_exit")) return ru ? "Вентиляция" : "Ventilation Shaft";
        if (lower.Contains("groundzero_secret_adaptation")) return ru ? "Кафе / Накаты" : "Scientist Taproom";
        if (lower.Contains("scav_exit")) return ru ? "Выход для Дикого" : "Scav Exit";
        if (lower.Contains("pmc_exit")) return ru ? "Выход для ЧВК" : "PMC Exit";

        string[] parts = rawName.Split(new char[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> cleanParts = new List<string>();

        foreach (string part in parts)
        {
            string lowerPart = part.ToLower();
            if (lowerPart == "exit" || lowerPart == "adaptation" || lowerPart == "pmc" || lowerPart == "scav")
            {
                continue;
            }

            if (part.Length > 0)
            {
                cleanParts.Add(char.ToUpper(part[0]) + part.Substring(1));
            }
        }

        string result = string.Join(" ", cleanParts);
        return string.IsNullOrEmpty(result) ? rawName : result;
    }

    private List<LootItem> GetQuestLoot()
    {
        var result = new List<LootItem>();
        try
        {
            var questItems = GetGameWorld()?.QuestItemsList;
            foreach (var loot in questItems ?? Enumerable.Empty<LootItem>())
            {
                if (loot != null) result.Add(loot);
            }
        }
        catch { }
        return result;
    }

    // Zone id -> quest name for every unfinished zone objective of the player's started quests.
    // Trigger ids in the scene only get a marker when a quest currently needs that zone.
    private Dictionary<string, string> GetActiveQuestZones()
    {
        var zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var quests = mainPlayer?.Profile?.QuestsData;
        if (quests == null) return zones;

        foreach (var quest in quests)
        {
            if (quest == null || quest.Status != EQuestStatus.Started) continue;

            // One broken quest shouldn't hide the others, or take the whole HUD down with it.
            try
            {
                var template = quest.Template;
                var conditions = template?.Conditions;
                if (conditions == null) continue;

                string questName = GetQuestName(template);
                foreach (var collection in conditions.Values)
                {
                    if (collection == null) continue;
                    foreach (var condition in collection)
                    {
                        CollectQuestZones(condition, quest, questName, zones, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!questReadFailureLogged)
                {
                    questReadFailureLogged = true;
                    Logger.LogWarning($"Compass HUD couldn't read objectives of quest {quest.Id}; skipping it (further failures not logged): {ex.Message}");
                }
            }
        }

        return zones;
    }

    private static void CollectQuestZones(Condition condition, QuestStatusData quest, string questName, Dictionary<string, string> zones, int depth)
    {
        if (condition == null || depth > 4) return;
        if (quest.CompletedConditions != null && quest.CompletedConditions.Contains(condition.id)) return;

        var zone = condition.TryCast<ConditionZone>();
        if (zone != null)
        {
            AddZones(zone.zoneIds, questName, zones);
            return;
        }

        var inZone = condition.TryCast<ConditionInZone>();
        if (inZone != null)
        {
            AddZones(inZone.zoneIds, questName, zones);
            return;
        }

        if (condition.TryCast<ConditionVisitPlace>() != null || condition.TryCast<ConditionLocationTrigger>() != null)
        {
            string target = condition.TryCast<ConditionOneTarget>()?.target;
            if (!string.IsNullOrEmpty(target)) zones[target] = questName;
            return;
        }

        var counter = condition.TryCast<ConditionCounterCreator>();
        if (counter?.Conditions != null)
        {
            foreach (var child in counter.Conditions)
            {
                CollectQuestZones(child, quest, questName, zones, depth + 1);
            }
        }
    }

    private static void AddZones(IEnumerable<string> zoneIds, string questName, Dictionary<string, string> zones)
    {
        if (zoneIds == null) return;
        foreach (var id in zoneIds)
        {
            if (!string.IsNullOrEmpty(id)) zones[id] = questName;
        }
    }

    private static string GetQuestName(QuestTemplate template)
    {
        string name = Localize(template.NameLocaleKey);
        if (!string.IsNullOrEmpty(name) && name != template.NameLocaleKey) return name;
        if (!string.IsNullOrEmpty(template.Name)) return Localize(template.Name);
        return "Quest Objective";
    }

    private string GetQuestLootItemName(LootItem lootItem)
    {
        if (lootItem == null) return "Quest Item";
        try
        {
            var item = lootItem.Item;
            if (item != null)
            {
                string name = item.LocalizedName();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
        }
        catch { }
        return "Quest Item";
    }

    private List<ExfiltrationPoint> GetEligibleExfils(GameWorld world)
    {
        var result = new List<ExfiltrationPoint>();
        if (world == null || mainPlayer == null || mainPlayer.Profile == null) return result;

        try
        {
            var controller = world.ExfiltrationController;
            if (controller != null)
            {
                var points = controller.EligiblePoints(mainPlayer.Profile);
                if (points != null)
                {
                    result.AddRange(points.Where(p => p != null));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Compass HUD couldn't query eligible exfils from the ExfiltrationController, falling back to a scene scan: {ex.Message}");
        }

        if (result.Count == 0)
        {
            try
            {
                var allExfils = UnityEngine.Object.FindObjectsOfType<ExfiltrationPoint>();
                string localEntryPoint = mainPlayer.Profile.Info?.EntryPoint?.ToLower();
                bool isScav = mainPlayer.Profile.Side == EPlayerSide.Savage;

                foreach (var point in allExfils)
                {
                    if (point == null || !point.gameObject.activeInHierarchy) continue;

                    bool isScavPoint = point.TryCast<ScavExfiltrationPoint>() != null;
                    if (isScav != isScavPoint) continue;

                    string statusStr = point.Status.ToString();
                    if (statusStr == "NotPresent" || statusStr == "Unknown") continue;

                    var entryPoints = point.EligibleEntryPoints;
                    if (!string.IsNullOrEmpty(localEntryPoint) && entryPoints != null && entryPoints.Any())
                    {
                        if (!entryPoints.Any(ep => ep != null && ep.ToLower() == localEntryPoint)) continue;
                    }

                    result.Add(point);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Compass HUD exfil scene scan failed: {ex.Message}");
            }
        }

        return result;
    }

    private static string GetTransitName(TransitPoint transit)
    {
        try
        {
            string name = transit.parameters?.name;
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }
        return "Transit";
    }

    private void RefreshActiveTargets()
    {
        try
        {
            RefreshActiveTargetsInternal();
        }
        catch (Exception ex)
        {
            LogFatal(nameof(RefreshActiveTargets), ex);
        }
    }

    private void RefreshActiveTargetsInternal()
    {
        activeMarkers.Clear();
        if (!isInRaid || mainPlayer == null || !markersInitialized) return;

        if (showExtractions.Value)
        {
            foreach (var point in cachedExfils)
            {
                if (point != null)
                {
                    string rawName = point.Settings?.Name;
                    string nameLower = (rawName ?? "").ToLower();
                    if (nameLower.Contains("secret") || nameLower.Contains("adaptation") || nameLower.Contains("test") || nameLower.Contains("note") || nameLower.Contains("scout"))
                    {
                        continue;
                    }

                    string statusStr = point.Status.ToString();
                    if (statusStr == "NotPresent" || statusStr == "Unknown")
                    {
                        continue;
                    }

                    activeMarkers.Add(new MarkerTarget
                    {
                        Position = point.transform.position,
                        Type = MarkerType.Extraction,
                        Name = LocalizeAndCleanName(rawName)
                    });
                }
            }
        }

        if (showTransits.Value)
        {
            foreach (var transit in cachedTransits)
            {
                if (transit != null && transit.gameObject.activeInHierarchy)
                {
                    activeMarkers.Add(new MarkerTarget
                    {
                        Position = transit.transform.position,
                        Type = MarkerType.Transit,
                        Name = LocalizeAndCleanName(GetTransitName(transit), transit.gameObject.name, true)
                    });
                }
            }
        }

        if (showQuests.Value)
        {
            var questZones = GetActiveQuestZones();
            foreach (var trigger in cachedQuestTriggers)
            {
                if (trigger == null || !trigger.gameObject.activeInHierarchy) continue;

                string zoneId = trigger.Id;
                if (string.IsNullOrEmpty(zoneId) || !questZones.TryGetValue(zoneId, out string questName)) continue;

                activeMarkers.Add(new MarkerTarget
                {
                    Position = trigger.transform.position,
                    Type = MarkerType.Quest,
                    Name = questName
                });
            }

            foreach (var loot in GetQuestLoot())
            {
                if (loot == null || !loot.gameObject.activeInHierarchy) continue;

                activeMarkers.Add(new MarkerTarget
                {
                    Position = loot.transform.position,
                    Type = MarkerType.Quest,
                    Name = GetQuestLootItemName(loot)
                });
            }
        }

        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            if (activeMarkers[i].Type == MarkerType.Transit)
            {
                bool nearExfil = false;
                for (int j = 0; j < activeMarkers.Count; j++)
                {
                    if (i != j && activeMarkers[j].Type == MarkerType.Extraction && Vector3.Distance(activeMarkers[i].Position, activeMarkers[j].Position) < 5f)
                    {
                        nearExfil = true;
                        break;
                    }
                }
                if (nearExfil)
                {
                    activeMarkers.RemoveAt(i);
                }
            }
        }

        for (int i = activeMarkers.Count - 1; i >= 0; i--)
        {
            bool overlap = false;
            for (int j = 0; j < i; j++)
            {
                if (activeMarkers[i].Type == activeMarkers[j].Type && Vector3.Distance(activeMarkers[i].Position, activeMarkers[j].Position) < 2f)
                {
                    overlap = true;
                    break;
                }
            }
            if (overlap)
            {
                activeMarkers.RemoveAt(i);
            }
        }
    }

    public void OnGUI()
    {
        if (fatalError || !resourcesCreated || !enabledCompass.Value || !isInRaid || mainPlayer == null || Cursor.visible)
            return;

        try
        {
            DrawCompassHUD();
        }
        catch (Exception ex)
        {
            LogFatal(nameof(OnGUI), ex);
        }
    }

    private void DrawCompassHUD()
    {
        float centerX = Screen.width / 2f;
        float width = 900f * scale.Value;
        bool markersActive = showExtractions.Value || showTransits.Value || showQuests.Value;
        float yOffset = (markersActive ? 4f : 8f) * scale.Value;
        float height = (markersActive ? 75f : 56f) * scale.Value;

        bool flip = labelsAboveTape.Value;

        // Flip-mode label stack: fixed, non-overlapping rows measured from topY upward,
        // independent of icon scaling so a large centered marker can't collide with the
        // rows above it. Row 1 (nearest the tape) = per-marker distance ("33m"), row 2 =
        // tick degrees/letters, row 3 (topmost) = the closest-target summary line.
        float rowGap = 3f * scale.Value;
        float subDistRowHeight = 16f * scale.Value;
        float tickRowHeight = 24f * scale.Value;
        float bottomRowHeight = 20f * scale.Value;
        float subDistTopOffset = rowGap + subDistRowHeight;
        float tickTopOffset = subDistTopOffset + rowGap + tickRowHeight;
        float bottomTopOffset = tickTopOffset + rowGap + bottomRowHeight;

        float maxHudHeight = flip ? (height + 6f) : (height + 4f + 20f * scale.Value);
        float minY = flip ? (bottomTopOffset + 6f) : 6f;
        float maxY = Screen.height - maxHudHeight - 6f;
        float topY = Mathf.Lerp(minY, maxY, compassYPosition.Value);

        float centralY = topY + (markersActive ? 14f : 8f) * scale.Value;

        Color oldColor = GUI.color;

        DrawLinearGradientBackground(centerX, width, topY, height);

        GUI.color = new Color(1f, 1f, 1f, opacity.Value);

        MarkerTarget closestCenterTarget = null;
        float closestDeltaAngle = float.MaxValue;
        float normYaw = Normalize(cachedYaw);

        currentFrameMarkerX.Clear();

        if (mainPlayer != null)
        {
            float pixelsPerDegree = (width / 80f);
            Vector3 pPos = mainPlayer.Position;

            for (int i = 0; i < activeMarkers.Count; i++)
            {
                MarkerTarget target = activeMarkers[i];
                Vector3 dir = target.Position - pPos;
                float ang = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                ang = Normalize(ang + 180f);

                float da = Mathf.DeltaAngle(normYaw, ang);
                if (Mathf.Abs(da) < closestDeltaAngle)
                {
                    closestDeltaAngle = Mathf.Abs(da);
                    if (closestDeltaAngle < 3.5f)
                    {
                        closestCenterTarget = target;
                        closestCenterTarget.CurrentDistance = Vector3.Distance(pPos, target.Position);
                    }
                }
            }

            for (int i = 0; i < activeMarkers.Count; i++)
            {
                MarkerTarget target = activeMarkers[i];
                Vector3 dir = target.Position - pPos;
                float ang = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                ang = Normalize(ang + 180f);

                float da = Mathf.DeltaAngle(normYaw, ang);
                if (Mathf.Abs(da) <= 40f)
                {
                    bool isCentered = (target == closestCenterTarget && closestDeltaAngle < 3.5f);
                    float finalX = isCentered ? centerX : (centerX + da * pixelsPerDegree);
                    currentFrameMarkerX.Add(finalX);
                }
            }
        }

        if (showCentralIndicator.Value)
        {
            string centralText = "";
            bool ru = UseRussian;

            if (closestCenterTarget != null && closestDeltaAngle < 3.5f)
            {
                centralText = "     ";
            }
            else
            {
                float[] majorAngles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
                string[] majorNamesEn = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                string[] majorNamesRu = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };

                bool isPointingAtLetter = false;
                for (int i = 0; i < majorAngles.Length; i++)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(normYaw, majorAngles[i])) <= 3.5f)
                    {
                        centralText = ru ? majorNamesRu[i] : majorNamesEn[i];
                        isPointingAtLetter = true;
                        break;
                    }
                }

                if (!isPointingAtLetter)
                    centralText = ((int)normYaw).ToString();
            }

            centralDegreeStyle.fontSize = Mathf.RoundToInt(26f * scale.Value);
            string centralDegree = "[ " + centralText + " ]";
            GUI.Label(new Rect(centerX - 150f * scale.Value, centralY, 300f * scale.Value, 40f * scale.Value), centralDegree, centralDegreeStyle);
        }

        DrawCompassLinear(centerX, topY, width, height, normYaw, yOffset, flip, tickTopOffset);

        if (mainPlayer != null)
        {
            float halfWidth = width / 2f;
            float pixelsPerDegree = (width / 80f);
            Vector3 playerPos = mainPlayer.Position;

            for (int i = 0; i < activeMarkers.Count; i++)
            {
                MarkerTarget target = activeMarkers[i];
                Texture2D tex = (target.Type == MarkerType.Quest) ? (questTexture ?? extractionTexture) : extractionTexture;
                bool usingFallbackIcon = tex == null;
                if (usingFallbackIcon) tex = lineTexture;

                Vector3 dirToTarget = target.Position - playerPos;
                float angleToTarget = Mathf.Atan2(dirToTarget.x, dirToTarget.z) * Mathf.Rad2Deg;
                if (angleToTarget < 0) angleToTarget += 360f;
                angleToTarget = Normalize(angleToTarget + 180f);

                float deltaAngle = Mathf.DeltaAngle(normYaw, angleToTarget);

                if (Mathf.Abs(deltaAngle) > 40f)
                    continue;

                bool isCentered = (target == closestCenterTarget && closestDeltaAngle < 3.5f);
                float x = isCentered ? centerX : (centerX + deltaAngle * pixelsPerDegree);

                if (x < centerX - halfWidth || x > centerX + halfWidth)
                    continue;

                float normDist = isCentered ? 0f : (Mathf.Abs(x - centerX) / halfWidth);
                float smoothPower = Mathf.Lerp(20f, 0.4f, Mathf.Clamp(roundedness.Value, 0f, 10f) / 10f);
                float smoothFactor = Mathf.Clamp01(1f - Mathf.Pow(normDist, smoothPower));
                float elementAlpha = smoothFactor * opacity.Value;

                if (target.Type == MarkerType.Extraction)
                    GUI.color = new Color(0.95f, 0.7f, 0f, elementAlpha);
                else if (target.Type == MarkerType.Transit)
                    GUI.color = new Color(1.0f, 0.15f, 0.15f, elementAlpha);
                else
                    GUI.color = new Color(0f, 0.95f, 0.15f, elementAlpha);

                float distance = Vector3.Distance(playerPos, target.Position);
                float clampedDistance = Mathf.Clamp(distance, 10f, 500f);

                float dynamicScale = 1.0f;
                if (isCentered)
                    dynamicScale = 1.4f;
                else if (!independentMarkerSize.Value)
                    dynamicScale = Mathf.Lerp(1.4f, 0.7f, (clampedDistance - 10f) / (500f - 10f));

                dynamicScale *= scale.Value * markerScale.Value;

                float iconSize = 40f * dynamicScale;
                if (target.Type == MarkerType.Quest)
                {
                    iconSize = 24f * dynamicScale;
                }
                if (usingFallbackIcon)
                {
                    iconSize *= 0.4f;
                }

                float iconY = (topY + 34f * scale.Value) - (iconSize / 2f);
                GUI.DrawTexture(new Rect(x - iconSize / 2f, iconY, iconSize, iconSize), tex);

                string subDistStr = Mathf.RoundToInt(distance) + "m";
                GUIStyle subStyle = subDistanceStyle;
                subStyle.fontSize = Mathf.RoundToInt(10f * dynamicScale);
                subStyle.normal.textColor = GUI.color;
                float subDistY = flip ? (topY - subDistTopOffset) : (iconY + iconSize + 1f);
                GUI.Label(new Rect(x - 50f * scale.Value, subDistY, 100f * scale.Value, 16f * scale.Value), subDistStr, subStyle);
            }
        }

        GUI.color = new Color(1f, 1f, 1f, opacity.Value);

        if (closestCenterTarget != null)
        {
            GUIStyle bottomStyle = bottomLabelStyle;
            bottomStyle.fontSize = Mathf.RoundToInt(16f * scale.Value);
            Color labelColor = Color.white;

            if (closestCenterTarget.Type == MarkerType.Transit)
                labelColor = new Color(1.0f, 0.15f, 0.15f, opacity.Value);
            else if (closestCenterTarget.Type == MarkerType.Extraction)
                labelColor = new Color(0.95f, 0.7f, 0f, opacity.Value);
            else
                labelColor = new Color(0f, 0.95f, 0.15f, opacity.Value);

            bottomStyle.normal.textColor = labelColor;

            string distStr = closestCenterTarget.Name + " [" + Mathf.RoundToInt(closestCenterTarget.CurrentDistance) + "m]";
            float bottomLabelY = flip ? (topY - bottomTopOffset) : (topY + height + 4f);
            GUI.Label(new Rect(centerX - 250f, bottomLabelY, 500f, 20f * scale.Value), distStr, bottomStyle);
        }

        GUI.color = oldColor;
    }

    private void DrawLinearGradientBackground(float centerX, float width, float topY, float height)
    {
        float halfWidth = width / 2f;
        float gradWidth = 50f * scale.Value;
        Color originalColor = GUI.color;

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(centerX - halfWidth, topY, gradWidth, height), leftGradient);

        GUI.color = new Color(0f, 0f, 0f, 0.18f);
        GUI.DrawTexture(new Rect(centerX - halfWidth + gradWidth, topY, width - 2f * gradWidth, height), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(centerX + halfWidth - gradWidth, topY, gradWidth, height), rightGradient);

        GUI.color = originalColor;
    }

    private void DrawCompassLinear(float centerX, float topY, float width, float height, float yaw, float yOffset, bool flip, float tickTopOffset)
    {
        float halfWidth = width / 2f;
        float pixelsPerDegree = (width / 80f);

        for (int angle = 0; angle < 360; angle += 5)
        {
            float relativeAngle = Mathf.DeltaAngle(yaw, angle);

            if (Mathf.Abs(relativeAngle) > 40f)
                continue;

            float x = centerX + relativeAngle * pixelsPerDegree;

            if (x < centerX - halfWidth || x > centerX + halfWidth)
                continue;

            if (showCentralIndicator.Value && Mathf.Abs(relativeAngle) < 4.0f)
                continue;

            bool isOverlappingMarker = false;
            foreach (float mx in currentFrameMarkerX)
            {
                if (Mathf.Abs(x - mx) < 22f * scale.Value)
                {
                    isOverlappingMarker = true;
                    break;
                }
            }

            float normDist = Mathf.Abs(x - centerX) / halfWidth;
            float smoothPower = Mathf.Lerp(20f, 0.4f, Mathf.Clamp(roundedness.Value, 0f, 10f) / 10f);
            float smoothFactor = Mathf.Clamp01(1f - Mathf.Pow(normDist, smoothPower));
            float elementAlpha = smoothFactor * opacity.Value;

            GUI.color = new Color(1f, 1f, 1f, elementAlpha);

            bool major = angle % 45 == 0;
            bool medium = angle % 15 == 0;

            float lineWidth = 1.5f * scale.Value;
            float lineHeight;

            if (major)
                lineHeight = 14.0f * smoothFactor * scale.Value;
            else if (medium)
                lineHeight = 9.0f * smoothFactor * scale.Value;
            else
                lineHeight = 6.0f * smoothFactor * scale.Value;

            if (!isOverlappingMarker)
            {
                GUI.DrawTexture(new Rect(x - lineWidth / 2f, topY + yOffset, lineWidth, lineHeight), lineTexture);
            }

            float textY = flip
                ? topY - tickTopOffset
                : topY + yOffset + lineHeight + 2f * scale.Value;

            if (isOverlappingMarker)
                continue;

            if (major)
            {
                if (showLetters.Value)
                {
                    string dir = GetDirection(angle);
                    if (dir != "")
                    {
                        compassStyle.fontSize = Mathf.RoundToInt(22f * smoothFactor * scale.Value);
                        GUI.Label(new Rect(x - 30f * scale.Value, textY, 60f * scale.Value, 24f * scale.Value), dir, compassStyle);
                    }
                }
                else if (showDegrees.Value)
                {
                    degreeStyle.fontSize = Mathf.RoundToInt(14f * smoothFactor * scale.Value);
                    GUI.Label(new Rect(x - 30f * scale.Value, textY, 60f * scale.Value, 28f * scale.Value), angle.ToString(), degreeStyle);
                }
            }
            else if (medium && showDegrees.Value)
            {
                degreeStyle.fontSize = Mathf.RoundToInt(14f * smoothFactor * scale.Value);
                GUI.Label(new Rect(x - 30f * scale.Value, textY, 60f * scale.Value, 20f * scale.Value), angle.ToString(), degreeStyle);
            }
        }
    }

    private string GetDirection(float angle)
    {
        bool ru = UseRussian;

        if (angle >= 337.5f || angle < 22.5f) return ru ? "С" : "N";
        if (angle >= 22.5f && angle < 67.5f) return ru ? "СВ" : "NE";
        if (angle >= 67.5f && angle < 112.5f) return ru ? "В" : "E";
        if (angle >= 112.5f && angle < 157.5f) return ru ? "ЮВ" : "SE";
        if (angle >= 157.5f && angle < 202.5f) return ru ? "Ю" : "S";
        if (angle >= 202.5f && angle < 247.5f) return ru ? "ЮЗ" : "SW";
        if (angle >= 247.5f && angle < 292.5f) return ru ? "З" : "W";
        if (angle >= 292.5f && angle < 337.5f) return ru ? "СЗ" : "NW";

        return "";
    }

    private float Normalize(float value)
    {
        while (value < 0) value += 360f;
        while (value >= 360f) value -= 360f;
        return value;
    }
}
