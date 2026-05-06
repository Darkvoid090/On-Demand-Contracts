using Mafi;
using Mafi.Collections;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.World.Contracts;
using Mafi.Core.World.Entities;
using Newtonsoft.Json;
using System;
using System.IO;
using HarmonyLib;
namespace OnDemandContracts;

public sealed class OnDemandContractsMod : IMod, IDisposable
{
    private static Harmony s_harmony;
    private static bool s_harmonyPatchApplied;
    public Option<IConfig> ModConfig => Option<IConfig>.None;
    public bool IsUiOnly => false;
    public ModManifest Manifest { get; }
    public ModJsonConfig JsonConfig { get; }
    public static OnDemandContractsMod Instance { get; private set; }
    public static string ModDirectory { get; private set; }
    public static string ContractsFilePath { get; private set; }
    public static ProtosDb StoredProtosDb { get; private set; }
    public static string DeletedContractsBackupFilePath { get; private set; }

    public OnDemandContractsMod(ModManifest manifest)
    {
        Instance = this;
        Manifest = manifest;
        JsonConfig = new ModJsonConfig(this);

        string rootDirectoryPath = Manifest.RootDirectoryPath;
        ModDirectory = Manifest.RootDirectoryPath;
        ContractsFilePath = Path.Combine(ModDirectory, "data", "OnDemandContracts.json");
        var rootModsDirectory = Path.GetDirectoryName(Manifest.RootDirectoryPath) ?? Manifest.RootDirectoryPath;
        DeletedContractsBackupFilePath = Path.Combine(rootModsDirectory, "OnDemandContracts_deleted_backup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ContractsFilePath));
        if (!s_harmonyPatchApplied)
        {
            s_harmony = new Harmony("OnDemandContracts.HudEntry");
            var assembly = typeof(OnDemandContractsMod).Assembly;

            s_harmony.PatchCategory(assembly, "OnDemandContractsHudEntry");
            s_harmony.PatchCategory(assembly, "OnDemandContractsSaveCompat");
            s_harmonyPatchApplied = true;
        }

    }

    public void Dispose() { }
    public void MigrateJsonConfig(VersionSlim version, Dict<string, object> dict) { }
    public void EarlyInit(DependencyResolver resolver) { }
    public void Initialize(DependencyResolver resolver, bool gameWasLoaded) { }
    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool wasLoaded) { }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        StoredProtosDb = registrator.PrototypesDb;
        TryCreateDefaultFile();
        RegisterCustomContracts(registrator.PrototypesDb);
    }

    public static void OpenTool() => InGameContractsButtonPatch.OpenWindow();

    private static void TryCreateDefaultFile()
    {
        if (File.Exists(ContractsFilePath))
        {
            return;
        }

        var file = new CustomContractsFile { };

        File.WriteAllText(ContractsFilePath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }
    private void RegisterContractsFromFile(ProtosDb protosDb, string path, bool allowVillageAttach)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        CustomContractsFile file;

        try
        {
            file = JsonConvert.DeserializeObject<CustomContractsFile>(
                File.ReadAllText(path)
            );
        }
        catch (Exception ex)
        {
            Log.Warning("[On-Demand Contracts] Invalid contracts JSON file " + path + ": " + ex.Message);
            return;
        }

        if (file?.Contracts == null)
            return;

        foreach (var contract in file.Contracts)
        {
            TryRegisterContract(protosDb, contract, allowVillageAttach);
        }
    }
    private void RegisterCustomContracts(ProtosDb protosDb)
    {
        RegisterContractsFromFile(protosDb, ContractsFilePath, allowVillageAttach: true);
        RegisterContractsFromFile(protosDb, DeletedContractsBackupFilePath, allowVillageAttach: false);
    }

    private bool TryRegisterContract(ProtosDb protosDb, CustomContractData data, bool allowVillageAttach)
    {
        try
        {
            var villageId = new WorldMapVillageProto.ID(data.VillageId);
            if (!protosDb.TryGetProto<WorldMapVillageProto>(villageId, out var village))
            {
                Log.Warning("[On-Demand Contracts] Village not found: " + data.VillageId);
                return false;
            }

            if (!protosDb.TryGetProto<ProductProto>(new ProductProto.ID(data.InputProductId), out var input))
            {
                Log.Warning("[On-Demand Contracts] Input product not found: " + data.InputProductId);
                return false;
            }

            if (!protosDb.TryGetProto<ProductProto>(new ProductProto.ID(data.OutputProductId), out var output))
            {
                Log.Warning("[On-Demand Contracts] Output product not found: " + data.OutputProductId);
                return false;
            }

            var contract = protosDb.Add(new ContractProto(
                new Proto.ID(data.Id),
                output.WithQuantity(data.OutputQuantity),
                input.WithQuantity(data.InputQuantity),
                data.UpointsPerMonth.Upoints(),
                data.UpointsPer100ProductsBought.Upoints(),
                data.RequiredReputation
            ));

            if (allowVillageAttach && !data.IsDeleted && data.IsEnabled)
            {
                village.Contracts = village.Contracts.Add(contract);
                Log.Info("[On-Demand Contracts] Added contract '" + data.Name + "' to " + data.VillageId);
            }
            else
            {
                Log.Info("[On-Demand Contracts] Registered hidden contract proto '" + data.Name + "' for save compatibility.");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "[On-Demand Contracts] Failed to register contract: " + data.Name);
            return false;
        }
    }
    private CustomContractsFile ReadDeletedBackupFile()
    {
        string path = OnDemandContractsMod.DeletedContractsBackupFilePath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new CustomContractsFile
            {

            };

        return JsonConvert.DeserializeObject<CustomContractsFile>(
            File.ReadAllText(path)
        ) ?? new CustomContractsFile();
    }

    private void WriteDeletedBackupFile(CustomContractsFile file)
    {
        File.WriteAllText(
            OnDemandContractsMod.DeletedContractsBackupFilePath,
            JsonConvert.SerializeObject(file, Formatting.Indented)
        );
    }

    private void AddToDeletedBackup(CustomContractData deletedContract)
    {
        var backup = ReadDeletedBackupFile();

        var existing = System.Linq.Enumerable.FirstOrDefault(
            backup.Contracts,
            c => c.Id == deletedContract.Id
        );

        if (existing != null)
            backup.Contracts.Remove(existing);

        backup.Contracts.Add(deletedContract);

        WriteDeletedBackupFile(backup);
    }
    public static class CustomContractsFileLoader
    {
        public static CustomContractsFile Read(string path)
        {
            if (!File.Exists(path))
                return null;

            return JsonConvert.DeserializeObject<CustomContractsFile>(
                File.ReadAllText(path)
            );
        }
    }
}
