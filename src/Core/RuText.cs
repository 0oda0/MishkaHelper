namespace MishkaHelper.Core;

/// <summary>Russian text helpers shared by Jarvis and the secretary.</summary>
public static class RuText
{
    static readonly Dictionary<string, int> Nums = new()
    {
        ["ноль"] = 0, ["один"] = 1, ["одну"] = 1, ["одна"] = 1, ["два"] = 2, ["две"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5, ["шесть"] = 6,
        ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10, ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13,
        ["четырнадцать"] = 14, ["пятнадцать"] = 15, ["шестнадцать"] = 16, ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
        ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80,
        ["девяносто"] = 90, ["сто"] = 100, ["полчаса"] = 30, ["максимум"] = 100, ["минимум"] = 0, ["половину"] = 50,
    };

    public static int? Number(string s)
    {
        s = s.Trim();
        if (int.TryParse(s.Split(' ')[0], out var d)) return d;
        int sum = 0; bool any = false;
        foreach (var w in s.Split(' '))
            if (Nums.TryGetValue(w, out var v)) { sum += v; any = true; }
            else if (any) break;
        return any ? sum : null;
    }

    public static string Clean(string s) =>
        new string(s.ToLowerInvariant().Replace('ё', 'е').Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '%' ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Aggregate("", (a, w) => a.Length == 0 ? w : a + " " + w);
}
