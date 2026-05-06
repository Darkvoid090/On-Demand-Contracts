using Mafi;
using Mafi.Collections;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.World.Entities;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;

namespace OnDemandContracts;

public class OnDemandContractsWindow : Window
{
    private readonly ProtosDb m_protosDb;
    private readonly Lyst<WorldMapVillageProto> m_villages = [];
    private readonly Lyst<ProductProto> m_products = [];

    private ScrollColumn m_contractsList;
    private bool m_showDeletedOnly = false;
    private bool m_deleteConfirmPending = false;

    private Dropdown<WorldMapVillageProto> m_villageDropdown;
    private Dropdown<ProductProto> m_inputDropdown;
    private Dropdown<ProductProto> m_outputDropdown;

    private TextField m_nameField, m_inputQtyField, m_outputQtyField, m_monthlyField, m_per100Field;
    private Dropdown<int> m_requiredReputationDropdown;
    private Toggle m_enabledToggle;
    private Label m_statusLabel;
    private string m_editingId;

    public OnDemandContractsWindow(ProtosDb protosDb) : base("On-Demand Contracts".AsLoc())
    {
        m_protosDb = protosDb;
        WindowSize(610.px(), Px.Auto);

        m_villages.AddRange(
            protosDb.All<WorldMapVillageProto>()
                .Where(v => v.Id.Value != "SettlementForShips")
                .OrderBy(v => ExtractNumber(v.Id.Value))
        );

        m_products.AddRange(
        from p in protosDb.All<ProductProto>()
        where IsValidContractProduct(p, protosDb)
        orderby p.Strings.Name.TranslatedString
        select p
        );

        BuildUI();
        MigrateSoftDeletedContractsToBackup();
        LoadContractsList();
    }
    private static bool IsValidContractProduct(ProductProto product, ProtosDb db)
    {
        string id = product.Id.Value;

        if (string.IsNullOrWhiteSpace(id))
            return false;

        // Internal / virtual products.
        if (id.Contains("Virtual"))
            return false;

        // Not real transportable cargo.
        if (id.Contains("PollutedAir"))
            return false;

        // Not a valid product.
        if (id.Contains("Unity"))
            return false;

        // Does not work.
        if (id.Contains("CargoShip"))
            return false;

        // Molten products are not storeable.
        if (id.Contains("Molten"))
            return false;

        // Exhaust and gas emissions should not be normal contracts.
        if (id.Contains("Exhaust"))
            return false;

        // Recyclables would be useless because of game recycling tracking system.
        if (id.Contains("Recyclables"))
            return false;

        // Removed from game, likely remained in code to preserve compatability.
        if (id.Contains("Bricks"))
            return false;

        // Cannot be stored in Cargo Dock.
        if (product.Id.Value == "Chicken")
            return false;
        
        if (id.Contains("Flower"))
        {
            // If product exists, DLC is effectively present.
            if (!db.TryGetProto<ProductProto>(product.Id, out _))
                return false;
        }
        return true;
    }
    private void UpdateTabHighlight()
    {
        if (m_activeTabButton == null || m_deletedTabButton == null)
            return;

        // Reset both to default by recreating text (removes previous color)
        m_activeTabButton.Color(null);
        m_deletedTabButton.Color(null);

        // Highlight active one only
        if (m_showDeletedOnly)
        {
            m_deletedTabButton.Color(Theme.DangerColor);
        }
        else
        {
            m_activeTabButton.Color(Theme.PositiveColor);
        }
    }
    private static int ExtractNumber(string id)
    {
        var digits = new string(id.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var num) ? num : 0;
    }
    private ButtonText m_activeTabButton;
    private ButtonText m_deletedTabButton;
    private string m_selectedContractId;
    private void BuildUI()
    {
        m_contractsList = new ScrollColumn();
        m_contractsList.Width(200.px());

        var listPanel = new PanelWithHeader("Contract List".AsLoc());
        var tabRow = new Row(4.pt()).AlignItemsCenter();
        m_activeTabButton = new ButtonText("+".AsLoc(), ShowActiveContracts)
        .Compact()
        .Width(90.px());
        m_activeTabButton.Tooltip("Show active contracts.".AsLoc());

        m_deletedTabButton = new ButtonText("X".AsLoc(), ShowDeletedContracts)
            .Compact()
            .Width(90.px());
        m_deletedTabButton.Tooltip("Show deleted contracts.".AsLoc());

        tabRow.Add(m_activeTabButton);
        tabRow.Add(m_deletedTabButton);
        UpdateTabHighlight();

        listPanel.BodyAdd(
            tabRow,
            new Column().Height(6.pt()),
            m_contractsList
        );

        m_villageDropdown = new Dropdown<WorldMapVillageProto>((v, i, d) => {
            if (v == null)
                return new Label("(None)".AsLoc());

            return new Label($"{v.Strings.Name.TranslatedString} ({v.Id.Value})".AsLoc());
        });

        m_villageDropdown
        .SetOptions(m_villages.ToArray())
        .SetSearchStringLookup(v => $"{v.Strings.Name.TranslatedString} {v.Id.Value}")
        .IncludeClearOption("(None)".AsLoc());

        m_inputDropdown = ProductDropdown();
        m_outputDropdown = ProductDropdown();

        m_nameField = Field("Enter Name Here");
        m_inputQtyField = Field("100");
        m_outputQtyField = Field("100");
        m_monthlyField = Field("0.4");
        m_per100Field = Field("1");

        m_requiredReputationDropdown = new Dropdown<int>((value, i, d) =>
            new Label(value.ToString().AsLoc())
        );
        m_requiredReputationDropdown.SetOptions(new[] { 0, 1, 2, 3 });
        m_requiredReputationDropdown.SetValue(0);

        m_enabledToggle = new Toggle(standalone: true);
        m_enabledToggle.Value(true);

        m_statusLabel = new Label("".AsLoc());

        var form = new Column(4.pt()).PaddingRight(10.pt());
        form.Add(Row("Name", m_nameField));
        form.Add(Row("Village", m_villageDropdown, "Set1: Basic settlement that is unlocked from game start, does not usually have contracts.\nSet2: Wood, Coal, Corn, Wheat, Vegetables, Sugarcane, Copper ore and Imported Goods.\nSet3: Slag disposal contracts, Limestone, Iron Ore, Copper Ore.\nSet4: Quartz, Diesel to Gold, Sulfur Disposal, Dirt and Bauxite.\nSet5: Servers Trade, Copper Ore, Quartz, Coal and Titanium ore.\nSet6: Oil and Fuel Gas Imports, Ammonia.\nSet7: Uranium Contracts."));
        form.Add(Row("Traded Product", m_inputDropdown, "The type of product the player provides."));
        form.Add(Row("Input Quantity", m_inputQtyField, "The amount of product the player provides."));
        form.Add(Row("Received Product", m_outputDropdown, "The type of product the player receives."));
        form.Add(Row("Output Quantity", m_outputQtyField, "The amount of product the player receives."));
        form.Add(Row("Unity Monthly", m_monthlyField, "Monthly Unity cost while the contract is active."));
        form.Add(Row("Cost Per Delivery", m_per100Field, "Unity cost per 100 products traded through this contract every time the ship departs."));
        form.Add(Row("Required Reputation", m_requiredReputationDropdown, "Minimum settlement reputation level required before the contract becomes available."));
        form.Add(Row("Enabled", m_enabledToggle, "Use to temporarily disable contracts instead of deleting them."));

        form.Add(m_statusLabel);

        var buttonsRow = new Row(4.pt());
        buttonsRow.Add(new ButtonText("Save Contract".AsLoc(), SaveContract).Compact().Width(110.px()));
        buttonsRow.Add(new ButtonText(Button.Danger, "Delete".AsLoc(), DeleteCurrent).Compact().Width(80.px()));
        var restoreButton = new ButtonText("Restore".AsLoc(), RestoreCurrent).Compact().Width(80.px());
        restoreButton.Color(Theme.PositiveColor);

        buttonsRow.Add(restoreButton);
        form.Add(buttonsRow);

        var editorPanel = new PanelWithHeader("Contract Editor".AsLoc());
        editorPanel.BodyAdd(form);

        var root = new Row(Window.WINDOW_GAP);
        root.Fill().AlignItemsStretch();
        root.Add(listPanel);
        root.Add(editorPanel);

        Body.Add(root);
    }

    private Dropdown<ProductProto> ProductDropdown()
    {
        var dd = new Dropdown<ProductProto>((p, i, d) => {
            if (p == null)
                return new Label("(None)".AsLoc());

            var row = new Row(2.pt()).AlignItemsCenter();
            row.Add(new Icon(p.Graphics.IconPath).Size(16.px()));
            row.Add(new Label(p.Strings.Name));
            return row;
        });

        dd.SetOptions(m_products.ToArray())
            .SetSearchStringLookup(p => p.Strings.Name.TranslatedString)
            .IncludeClearOption("(None)".AsLoc());

        return dd;
    }

    private static TextField Field(string placeholder)
    {
        var f = new TextField();
        f.Width(200.px()).Placeholder(placeholder.AsLoc());
        return f;
    }

    private static Row Row(string label, UiComponent component, string info = null)
    {
        var r = new Row(4.pt()).AlignItemsCenter();
        var labelComponent = new Label((label + ":").AsLoc()).Width(140.px());

        if (!string.IsNullOrWhiteSpace(info))
            labelComponent.Tooltip(info.AsLoc());

        r.Add(labelComponent);
        r.Add(component);
        return r;
    }
    private CustomContractsFile ReadDeletedBackupFile()
    {
        string path = OnDemandContractsMod.DeletedContractsBackupFilePath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new CustomContractsFile
            {
            };
        }

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
    private CustomContractsFile ReadFile()
    {
        if (!File.Exists(OnDemandContractsMod.ContractsFilePath))
            return new CustomContractsFile();

        return JsonConvert.DeserializeObject<CustomContractsFile>(
            File.ReadAllText(OnDemandContractsMod.ContractsFilePath)
        ) ?? new CustomContractsFile();
    }

    private void WriteFile(CustomContractsFile file)
    {
        File.WriteAllText(
            OnDemandContractsMod.ContractsFilePath,
            JsonConvert.SerializeObject(file, Formatting.Indented)
        );
    }

    private void LoadContractsList()
    {
        m_contractsList.Clear();

        CustomContractsFile file = m_showDeletedOnly
            ? ReadDeletedBackupFile()
            : ReadFile();

        var contracts = file.Contracts;

        foreach (var c in contracts)
        {
            var label = m_showDeletedOnly
                ? c.Name + " (deleted)"
                : c.IsEnabled ? c.Name : c.Name + " (off)";

            var button = new ButtonText(label.AsLoc(), () => LoadIntoEditor(c))
                .Compact()
                .Width(180.px())
                .MarginBottom(2.pt());

            if (c.Id == m_selectedContractId)
            {
                button.Color(m_showDeletedOnly ? Theme.DangerColor : Theme.PositiveColor);
            }

            m_contractsList.Add(button);
        }
    }

    private void ShowActiveContracts()
    {
        m_showDeletedOnly = false;
        UpdateTabHighlight();
        ClearEditor();
        LoadContractsList();
        Status("Showing active contracts.", false);
    }

    private void ShowDeletedContracts()
    {
        m_showDeletedOnly = true;
        UpdateTabHighlight();
        ClearEditor();
        LoadContractsList();
        Status("Showing deleted contracts.", false);
    }

    private void LoadIntoEditor(CustomContractData c)
    {
        m_editingId = c.Id;
        m_selectedContractId = c.Id;
        m_deleteConfirmPending = false;

        LoadContractsList();
        m_nameField.Text(c.Name.AsLoc());

        SetDropdown(
            m_villageDropdown,
            System.Linq.Enumerable.FirstOrDefault(
                m_villages.ToArray(),
                v => v.Id.Value == c.VillageId
            )
        );

        SetDropdown(
            m_inputDropdown,
            System.Linq.Enumerable.FirstOrDefault(
                m_products.ToArray(),
                p => p.Id.Value == c.InputProductId
            )
        );

        SetDropdown(
            m_outputDropdown,
            System.Linq.Enumerable.FirstOrDefault(
                m_products.ToArray(),
                p => p.Id.Value == c.OutputProductId
            )
        );

        m_inputQtyField.Text(c.InputQuantity.ToString().AsLoc());
        m_outputQtyField.Text(c.OutputQuantity.ToString().AsLoc());
        m_monthlyField.Text(c.UpointsPerMonth.ToString().AsLoc());
        m_per100Field.Text(c.UpointsPer100ProductsBought.ToString().AsLoc());

        int requiredReputation = c.RequiredReputation;
        if (requiredReputation < 0)
            requiredReputation = 0;
        if (requiredReputation > 3)
            requiredReputation = 3;

        m_requiredReputationDropdown.SetValue(requiredReputation);
        m_enabledToggle.Value(c.IsEnabled);
    }
    private void MigrateSoftDeletedContractsToBackup()
    {
        var file = ReadFile();

        var deleted = file.Contracts.Where(c => c.IsDeleted).ToList();
        if (deleted.Count == 0)
            return;

        var backup = ReadDeletedBackupFile();

        foreach (var contract in deleted)
        {
            var existing = System.Linq.Enumerable.FirstOrDefault(
                backup.Contracts,
                c => c.Id == contract.Id
            );

            if (existing != null)
                backup.Contracts.Remove(existing);

            contract.IsDeleted = true;
            contract.IsEnabled = false;
            backup.Contracts.Add(contract);
        }

        file.Contracts.RemoveAll(c => c.IsDeleted);

        WriteFile(file);
        WriteDeletedBackupFile(backup);
    }
    private static void SetDropdown<T>(Dropdown<T> dd, T value) where T : class
    {
        if (value == null)
            dd.SetValueIndex(0);
        else
            dd.SetValue(value);
    }

    private void ClearEditor()
    {
        m_editingId = null;
        m_deleteConfirmPending = false;
        m_selectedContractId = null;
        m_nameField.Text("".AsLoc());
        m_inputQtyField.Text("".AsLoc());
        m_outputQtyField.Text("".AsLoc());
        m_monthlyField.Text("".AsLoc());
        m_per100Field.Text("".AsLoc());

        m_villageDropdown.SetValueIndex(0);
        m_inputDropdown.SetValueIndex(0);
        m_outputDropdown.SetValueIndex(0);

        m_requiredReputationDropdown.SetValue(0);
        m_enabledToggle.Value(true);

        m_statusLabel.Value("".AsLoc());
        m_nameField.Focus();
    }

    private void SaveContract()
    {
        if (
            m_villageDropdown.SelectedValue == null ||
            m_inputDropdown.SelectedValue == null ||
            m_outputDropdown.SelectedValue == null
        )
        {
            Status("Select village, input product, and output product.", true);
            return;
        }

        if (
            !int.TryParse(m_inputQtyField.GetText(), out int inQty) ||
            !int.TryParse(m_outputQtyField.GetText(), out int outQty)
        )
        {
            Status("Quantities must be whole numbers.", true);
            return;
        }

        if (inQty <= 0 || outQty <= 0)
        {
            Status("Quantities must be greater than 0.", true);
            return;
        }

        double.TryParse(m_monthlyField.GetText(), out double monthly);
        double.TryParse(m_per100Field.GetText(), out double per100);

        var file = ReadFile();

        var c = m_editingId == null
            ? null
            : System.Linq.Enumerable.FirstOrDefault(file.Contracts, x => x.Id == m_editingId);

        if (c != null && c.IsDeleted)
        {
            Status("Restore this contract before saving changes.", true);
            return;
        }

        if (c == null)
        {
            c = new CustomContractData
            {
                Id = "cm_contract_" + DateTime.UtcNow.Ticks
            };

            file.Contracts.Add(c);
            m_editingId = c.Id;
        }

        c.Name = string.IsNullOrWhiteSpace(m_nameField.GetText())
            ? c.Id
            : m_nameField.GetText();

        c.VillageId = m_villageDropdown.SelectedValue.Id.Value;
        c.InputProductId = m_inputDropdown.SelectedValue.Id.Value;
        c.OutputProductId = m_outputDropdown.SelectedValue.Id.Value;

        c.InputQuantity = inQty;
        c.OutputQuantity = outQty;
        c.UpointsPerMonth = monthly;
        c.UpointsPer100ProductsBought = per100;

        c.RequiredReputation = m_requiredReputationDropdown.SelectedValue;
        c.IsEnabled = m_enabledToggle.GetValue();
        c.IsDeleted = false;

        WriteFile(file);
        LoadContractsList();

        Status("Saved. Restart/Reload the game to apply.", false);
    }

    private void DeleteCurrent()
    {
        if (m_editingId == null)
        {
            Status("No contract selected.", true);
            return;
        }

        var file = ReadFile();

        var contract = System.Linq.Enumerable.FirstOrDefault(
            file.Contracts,
            c => c.Id == m_editingId
        );

        if (contract == null)
        {
            Status("Could not find contract.", true);
            return;
        }

        if (!m_deleteConfirmPending)
        {
            m_deleteConfirmPending = true;
            Status("Click Delete again to confirm.", true);
            return;
        }

        var backup = ReadDeletedBackupFile();

        var existingBackup = System.Linq.Enumerable.FirstOrDefault(
            backup.Contracts,
            c => c.Id == contract.Id
        );

        if (existingBackup != null)
        {
            backup.Contracts.Remove(existingBackup);
        }

        contract.IsDeleted = true;
        contract.IsEnabled = false;
        backup.Contracts.Add(contract);

        file.Contracts.Remove(contract);

        WriteFile(file);
        WriteDeletedBackupFile(backup);

        m_deleteConfirmPending = false;
        ClearEditor();
        LoadContractsList();

        Status("Contract moved to deleted backup.", false);
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

    private void RestoreCurrent()
    {
        if (m_editingId == null)
        {
            Status("No contract selected.", true);
            return;
        }

        var backup = ReadDeletedBackupFile();

        var contract = System.Linq.Enumerable.FirstOrDefault(
            backup.Contracts,
            c => c.Id == m_editingId
        );

        if (contract == null)
        {
            Status("Could not find contract in backup.", true);
            return;
        }

        var file = ReadFile();

        var existingMain = System.Linq.Enumerable.FirstOrDefault(
            file.Contracts,
            c => c.Id == contract.Id
        );

        if (existingMain != null)
        {
            file.Contracts.Remove(existingMain);
        }

        contract.IsDeleted = false;
        contract.IsEnabled = true;

        file.Contracts.Add(contract);
        backup.Contracts.Remove(contract);

        WriteFile(file);
        WriteDeletedBackupFile(backup);

        m_showDeletedOnly = false;
        LoadContractsList();
        LoadIntoEditor(contract);

        Status("Contract restored. Restart/Reload required.", false);
    }

    private void Status(string text, bool error)
    {
        m_statusLabel.Value(text.AsLoc());
        m_statusLabel.Color(error ? Theme.DangerColor : Theme.PositiveColor);
    }
}