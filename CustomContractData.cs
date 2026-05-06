using System;
using System.Collections.Generic;

namespace OnDemandContracts;

[Serializable]
public class CustomContractsFile {

    public List<CustomContractData> Contracts { get; set; } = [];
   
}

[Serializable]
public class CustomContractData {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New Contract";
    public string VillageId { get; set; } = "";
    public string InputProductId { get; set; } = "";
    public int InputQuantity { get; set; } = 100;
    public string OutputProductId { get; set; } = "";
    public int OutputQuantity { get; set; } = 100;
    public double UpointsPerMonth { get; set; } = 0.4;
    public double UpointsPer100ProductsBought { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public int RequiredReputation { get; set; } = 1;
}
