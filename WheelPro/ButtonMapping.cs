namespace WheelPro;

public static class ButtonMapping
{
    public static void AssignUnique(IDictionary<string, uint> mappings, string control, uint rawMask)
    {
        foreach (var duplicate in mappings.Where(binding => binding.Value == rawMask &&
                     !binding.Key.Equals(control, StringComparison.OrdinalIgnoreCase)).Select(binding => binding.Key).ToArray())
            mappings.Remove(duplicate);
        mappings[control] = rawMask;
    }

    public static void RemoveDuplicateBindings(IDictionary<string, uint> mappings)
    {
        var duplicateMasks = mappings.GroupBy(binding => binding.Value)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        foreach (var duplicate in mappings.Where(binding => duplicateMasks.Contains(binding.Value)).Select(binding => binding.Key).ToArray())
            mappings.Remove(duplicate);
    }
}
