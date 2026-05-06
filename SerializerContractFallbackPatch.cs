using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.World.Contracts;
using Newtonsoft.Json;

namespace OnDemandContracts;

[HarmonyPatch(typeof(ProtosSerializerFactory))]
[HarmonyPatchCategory("OnDemandContractsSaveCompat")]
public static class SerializerContractFallbackPatch
{
    private const string PhantomPrefix = "__PHANTOM__FAILED_TO_LOAD__";
    private const string ContractPrefix = "cm_contract_";

    private static readonly FieldInfo s_protosDbField =
        typeof(ProtosSerializerFactory).GetField(
            "m_protosDb",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    [HarmonyPostfix]
    [HarmonyPatch("deserializeNewProto")]
    public static void Postfix(ref Proto __result, ProtosSerializerFactory __instance)
    {
        if (!(__result is InvalidProto))
            return;

        string phantomId = __result.Id.Value;

        if (!phantomId.StartsWith(PhantomPrefix))
            return;

        string originalId = phantomId.Substring(PhantomPrefix.Length);

        if (!originalId.StartsWith(ContractPrefix))
            return;

        if (!(s_protosDbField?.GetValue(__instance) is ProtosDb protosDb))
        {
            Log.Error("[On-Demand Contracts] Fallback failed: could not access ProtosDb.");
            return;
        }

        var data = FindContractData(originalId);

        if (data == null)
        {
            Log.Error("[On-Demand Contracts] Fallback failed: no backup data for missing contract '" + originalId + "'.");
            return;
        }

        if (!TryCreateContractProto(protosDb, data, out var replacement))
            return;

        __result = replacement;

        try
        {
            var dict = ReflectionExtensions.UniversalGetPrivateProperty<Dict<Proto.ID, Proto>>(
                protosDb,
                "m_protoById"
            );

            if (dict != null && !dict.ContainsKey(new Proto.ID(originalId)))
            {
                dict[new Proto.ID(originalId)] = replacement;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[On-Demand Contracts] Fallback injection failed: " + ex.Message);
        }

        Log.Info("[On-Demand Contracts] Fallback restored missing contract proto '" + originalId + "'.");
    }

    private static CustomContractData FindContractData(string id)
    {
        var main = FindInFile(OnDemandContractsMod.ContractsFilePath, id);
        if (main != null)
            return main;

        return FindInFile(OnDemandContractsMod.DeletedContractsBackupFilePath, id);
    }

    private static CustomContractData FindInFile(string path, string id)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var file = JsonConvert.DeserializeObject<CustomContractsFile>(
                File.ReadAllText(path)
            );

            return System.Linq.Enumerable.FirstOrDefault(
                file?.Contracts,
                c => c.Id == id
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCreateContractProto(
        ProtosDb protosDb,
        CustomContractData data,
        out ContractProto contract
    )
    {
        contract = null;

        if (!protosDb.TryGetProto<ProductProto>(new ProductProto.ID(data.InputProductId), out var input))
            return false;

        if (!protosDb.TryGetProto<ProductProto>(new ProductProto.ID(data.OutputProductId), out var output))
            return false;

        contract = new ContractProto(
            new Proto.ID(data.Id),
            output.WithQuantity(data.OutputQuantity),
            input.WithQuantity(data.InputQuantity),
            data.UpointsPerMonth.Upoints(),
            data.UpointsPer100ProductsBought.Upoints(),
            data.RequiredReputation
        );

        return true;
    }
}