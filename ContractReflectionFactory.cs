using System;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Products;
using Mafi.Core.World.Contracts;

namespace OnDemandContracts;

internal static class ContractReflectionFactory {
    public static ContractProto CreateContract(ProductProto input, int inputQty, ProductProto output, int outputQty, double establish, double monthly, double per100, int scaling) {
        // This deliberately supports several COI constructor shapes, because ContractProto changed between versions.
        var ctors = typeof(ContractProto).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var ctor in ctors.OrderByDescending(c => c.GetParameters().Length)) {
            var p = ctor.GetParameters();
            object[] args = TryBuildArgs(p, input, inputQty, output, outputQty, establish, monthly, per100, scaling);
            if (args == null) continue;
            try { return (ContractProto)ctor.Invoke(args); } catch { }
        }
        return null;
    }

    private static object[] TryBuildArgs(ParameterInfo[] p, ProductProto input, int inputQty, ProductProto output, int outputQty, double establish, double monthly, double per100, int scaling) {
        object[] args = new object[p.Length];
        int productSeen = 0;
        int qtySeen = 0;
        int doubleSeen = 0;
        int intSeen = 0;
        for (int i = 0; i < p.Length; i++) {
            Type t = p[i].ParameterType;
            string n = p[i].Name?.ToLowerInvariant() ?? "";
            if (typeof(ProductProto).IsAssignableFrom(t)) {
                args[i] = productSeen++ == 0 ? input : output;
            } else if (t == typeof(ProductProto.ID)) {
                args[i] = productSeen++ == 0 ? input.Id : output.Id;
            } else if (t == typeof(Quantity)) {
                args[i] = (qtySeen++ == 0 ? inputQty : outputQty).Quantity();
            } else if (t.Name == "ProductQuantity") {
                args[i] = Activator.CreateInstance(t, productSeen++ == 0 ? input : output, (qtySeen++ == 0 ? inputQty : outputQty).Quantity());
            } else if (t == typeof(double) || t == typeof(float)) {
                double v = n.Contains("month") ? monthly : n.Contains("100") ? per100 : n.Contains("establish") || n.Contains("fee") ? establish : (doubleSeen++ == 0 ? establish : doubleSeen == 1 ? monthly : per100);
                args[i] = t == typeof(float) ? (object)(float)v : v;
            } else if (t == typeof(int)) {
                args[i] = n.Contains("scal") ? scaling : (intSeen++ < 2 ? (qtySeen++ == 0 ? inputQty : outputQty) : scaling);
            } else if (t == typeof(bool)) {
                args[i] = false;
            } else if (p[i].HasDefaultValue) {
                args[i] = p[i].DefaultValue;
            } else {
                return null;
            }
        }
        return args;
    }
}
